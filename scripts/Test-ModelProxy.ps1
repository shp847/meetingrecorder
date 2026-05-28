param(
    [string]$BaseUrl = "http://127.0.0.1:8645/v1",
    [string]$ApiKey = $env:MODELPROXY_MEETING_RECORDER_API_KEY,
    [string]$Model = $null
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Net.Http

$normalizedBaseUrl = $BaseUrl.TrimEnd("/")
$parallelRequestCount = 5
$maxBackendBusyAttempts = 3

if (-not $ApiKey) {
    $ApiKey = "sk-modelproxy"
}

function Get-ModelProxyErrorKind {
    param([string]$ResponseContent)

    if (-not $ResponseContent) {
        return $null
    }

    try {
        $payload = $ResponseContent | ConvertFrom-Json
        $errorNode = $payload.detail
        if (-not $errorNode) {
            $errorNode = $payload.error
        }

        if (-not $errorNode) {
            return $null
        }

        foreach ($propertyName in @("type", "category", "code")) {
            $value = $errorNode.$propertyName
            if ($value) {
                return [string]$value
            }
        }
    }
    catch {
        return $null
    }

    return $null
}

function New-ModelProxySmokeRequest {
    param(
        [string]$ModelName,
        [bool]$UseLongSyntheticPrompt = $false
    )

    $prompt = "Reply exactly: summary-provider-ok"
    if ($UseLongSyntheticPrompt) {
        $prompt = $prompt + ". Synthetic validation context: " + ("parallel no-search app-server validation. " * 160)
    }

    $body = @{
        model = $ModelName
        messages = @(@{ role = "user"; content = $prompt })
    } | ConvertTo-Json -Depth 8

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Post,
        "$normalizedBaseUrl/chat/completions")
    $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $ApiKey)
    $request.Headers.TryAddWithoutValidation("X-ModelProxy-Backend", "app-server") | Out-Null
    $request.Headers.TryAddWithoutValidation("X-ModelProxy-Web-Search", "false") | Out-Null
    $request.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, "application/json")
    return $request
}

function Read-ModelProxySmokeResponse {
    param(
        [System.Net.Http.HttpResponseMessage]$Response,
        [int]$RequestIndex
    )

    $responseContent = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $routing = @{}
    foreach ($headerName in @(
        "X-ModelProxy-Request-Id",
        "X-ModelProxy-Requested-Backend",
        "X-ModelProxy-Effective-Backend",
        "X-ModelProxy-Web-Search-Backend",
        "X-ModelProxy-App-Server-Web-Search-Supported",
        "X-ModelProxy-Fallback-Reason")) {
        if ($Response.Headers.Contains($headerName)) {
            $headerValue = [string]::Join(",", $Response.Headers.GetValues($headerName))
            if ($headerValue) {
                $routing[$headerName] = $headerValue
            }
        }
    }

    if (-not $Response.IsSuccessStatusCode) {
        $errorKind = Get-ModelProxyErrorKind -ResponseContent $responseContent
        return [pscustomobject]@{
            RequestIndex = $RequestIndex
            Success = $false
            RetryableBackendBusy = ($errorKind -eq "backend_busy")
            ErrorKind = $errorKind
            Routing = $routing
        }
    }

    $payload = $responseContent | ConvertFrom-Json
    $content = $payload.choices[0].message.content
    if ($content -notmatch "summary-provider-ok") {
        throw "Unexpected ModelProxy response for synthetic request $RequestIndex."
    }

    return [pscustomobject]@{
        RequestIndex = $RequestIndex
        Success = $true
        RetryableBackendBusy = $false
        ErrorKind = $null
        Routing = $routing
    }
}

function Invoke-ParallelModelProxySmokeRequests {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$ModelName
    )

    $entries = @()
    for ($index = 1; $index -le $parallelRequestCount; $index++) {
        $request = New-ModelProxySmokeRequest -ModelName $ModelName -UseLongSyntheticPrompt:($index -eq $parallelRequestCount)
        $entries += [pscustomobject]@{
            Index = $index
            Request = $request
            Task = $Client.SendAsync($request)
        }
    }

    [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($entries.Task))

    $results = @()
    foreach ($entry in $entries) {
        $response = $null
        try {
            $response = $entry.Task.GetAwaiter().GetResult()
            $results += Read-ModelProxySmokeResponse -Response $response -RequestIndex $entry.Index
        }
        finally {
            if ($response) {
                $response.Dispose()
            }

            $entry.Request.Dispose()
        }
    }

    return $results
}

function Invoke-ModelProxySmokeRetry {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$ModelName,
        [int]$RequestIndex,
        [int]$Attempt
    )

    $request = $null
    $response = $null
    try {
        $request = New-ModelProxySmokeRequest -ModelName $ModelName -UseLongSyntheticPrompt:($RequestIndex -eq $parallelRequestCount)
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        return Read-ModelProxySmokeResponse -Response $response -RequestIndex $RequestIndex
    }
    finally {
        if ($response) {
            $response.Dispose()
        }

        if ($request) {
            $request.Dispose()
        }
    }
}

$httpClient = [System.Net.Http.HttpClient]::new()
try {
    $httpClient.Timeout = [TimeSpan]::FromSeconds(900)
    if (-not $Model) {
        $modelsRequest = [System.Net.Http.HttpRequestMessage]::new(
            [System.Net.Http.HttpMethod]::Get,
            "$normalizedBaseUrl/models")
        $modelsRequest.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $ApiKey)
        $modelsResponse = $null
        try {
            $modelsResponse = $httpClient.SendAsync($modelsRequest).GetAwaiter().GetResult()
            $modelsContent = $modelsResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $modelsResponse.IsSuccessStatusCode) {
                throw "ModelProxy models request failed with HTTP $([int]$modelsResponse.StatusCode) $($modelsResponse.ReasonPhrase)."
            }

            $modelsPayload = $modelsContent | ConvertFrom-Json
            $Model = $modelsPayload.default_model
            if (-not $Model -and $modelsPayload.data) {
                $defaultEntry = @($modelsPayload.data | Where-Object { $_.default } | Select-Object -First 1)
                if ($defaultEntry.Count -gt 0) {
                    $Model = $defaultEntry[0].id
                }
            }
            if (-not $Model -and $modelsPayload.data) {
                $firstEntry = @($modelsPayload.data | Select-Object -First 1)
                if ($firstEntry.Count -gt 0) {
                    $Model = $firstEntry[0].id
                }
            }
            if (-not $Model) {
                throw "ModelProxy models response did not include a usable default model."
            }
            Write-Host "Meeting Recorder ModelProxy default model: $Model"
        }
        finally {
            if ($modelsResponse) {
                $modelsResponse.Dispose()
            }

            $modelsRequest.Dispose()
        }
    }

    $results = Invoke-ParallelModelProxySmokeRequests -Client $httpClient -ModelName $Model
    for ($attempt = 2; $attempt -le $maxBackendBusyAttempts; $attempt++) {
        $busyResults = @($results | Where-Object { $_.RetryableBackendBusy })
        if ($busyResults.Count -eq 0) {
            break
        }

        Write-Host "ModelProxy app-server reported retryable backend_busy for $($busyResults.Count) synthetic request(s); retrying shortly."
        Start-Sleep -Milliseconds (250 * ($attempt - 1))
        $updatedResults = @($results | Where-Object { -not $_.RetryableBackendBusy })
        foreach ($busyResult in $busyResults) {
            $updatedResults += Invoke-ModelProxySmokeRetry -Client $httpClient -ModelName $Model -RequestIndex $busyResult.RequestIndex -Attempt $attempt
        }

        $results = $updatedResults
    }

    $remainingBusy = @($results | Where-Object { $_.RetryableBackendBusy })
    if ($remainingBusy.Count -gt 0) {
        throw "ModelProxy app-server remained temporarily saturated after retry attempts; retry shortly."
    }

    $failedResults = @($results | Where-Object { -not $_.Success })
    if ($failedResults.Count -gt 0) {
        $firstFailure = $failedResults[0]
        throw "ModelProxy smoke failed for synthetic request $($firstFailure.RequestIndex) with structured error '$($firstFailure.ErrorKind)'."
    }

    Write-Host "Meeting Recorder ModelProxy synthetic smoke response matched expected marker for $parallelRequestCount parallel no-search app-server request(s)."

    $printedRouting = $false
    foreach ($result in $results) {
        foreach ($headerName in $result.Routing.Keys) {
            Write-Host "ModelProxy routing: request=$($result.RequestIndex) $headerName=$($result.Routing[$headerName])"
            $printedRouting = $true
        }
    }

    if (-not $printedRouting) {
        Write-Host "ModelProxy routing: no optional routing headers were returned."
    }
}
finally {
    $httpClient.Dispose()
}
