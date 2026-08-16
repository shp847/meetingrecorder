param(
    [string]$BaseUrl = "http://127.0.0.1:8645/v1",
    [string]$ApiKey = $env:MODELPROXY_MEETING_RECORDER_API_KEY,
    [string]$Model = $null
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Net.Http

$normalizedBaseUrl = $BaseUrl.TrimEnd("/")
$maxAttempts = 3
$requestTimeoutSeconds = 120

if (-not $ApiKey) {
    $ApiKey = "sk-modelproxy-meeting-recorder"
}

function Get-ModelProxyErrorDetails {
    param([string]$ResponseContent)

    if (-not $ResponseContent) {
        return $null
    }

    try {
        $payload = $ResponseContent | ConvertFrom-Json
        $errorNode = $payload.detail
        if ($errorNode -and $errorNode.error) {
            $errorNode = $errorNode.error
        }
        if (-not $errorNode) {
            $errorNode = $payload.error
        }

        if (-not $errorNode) {
            return $null
        }

        foreach ($propertyName in @("type", "category", "code")) {
            $value = $errorNode.$propertyName
            if ($value) {
                return [pscustomobject]@{
                    Kind = [string]$value
                    Retryable = [bool]$errorNode.retryable
                }
            }
        }
    }
    catch {
        return $null
    }

    return $null
}

function New-ModelProxySmokeRequest {
    param([string]$ModelName)

    $prompt = "Reply exactly: summary-provider-ok"

    $body = @{
        model = $ModelName
        input = @(@{
                role = "user"
                content = @(@{
                        type = "input_text"
                        text = $prompt
                    })
            })
    } | ConvertTo-Json -Depth 8

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Post,
        "$normalizedBaseUrl/responses")
    $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $ApiKey)
    $request.Headers.TryAddWithoutValidation("X-ModelProxy-Backend", "codex") | Out-Null
    $request.Headers.TryAddWithoutValidation("X-ModelProxy-Web-Search", "false") | Out-Null
    $request.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, "application/json")
    return $request
}

function Get-ModelProxyOutputText {
    param([object]$Payload)

    if (-not $Payload.output) {
        return $null
    }

    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($Payload.output)) {
        if ($item.type -ne "message" -or $item.role -ne "assistant" -or -not $item.content) {
            continue
        }

        foreach ($contentItem in @($item.content)) {
            if ($contentItem.type -eq "output_text" -and $contentItem.text) {
                $parts.Add([string]$contentItem.text)
            }
        }
    }

    if ($parts.Count -eq 0) {
        return $null
    }

    return [string]::Join("", $parts)
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
        $errorDetails = Get-ModelProxyErrorDetails -ResponseContent $responseContent
        return [pscustomobject]@{
            RequestIndex = $RequestIndex
            Success = $false
            RetryableBackendBusy = ($errorDetails -and $errorDetails.Kind -eq "backend_busy" -and $errorDetails.Retryable -and [int]$Response.StatusCode -in @(502, 503))
            ErrorKind = if ($errorDetails) { $errorDetails.Kind } else { $null }
            Routing = $routing
        }
    }

    $payload = $responseContent | ConvertFrom-Json
    $content = Get-ModelProxyOutputText -Payload $payload
    if ($content -notmatch "summary-provider-ok") {
        throw "Unexpected ModelProxy response for synthetic request $RequestIndex."
    }

    return [pscustomobject]@{
        Success = $true
        RetryableBackendBusy = $false
        ErrorKind = $null
        Routing = $routing
    }
}

function Invoke-ModelProxySmokeRetry {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$ModelName,
        [int]$Attempt
    )

    $request = $null
    $response = $null
    try {
        $request = New-ModelProxySmokeRequest -ModelName $ModelName
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        return Read-ModelProxySmokeResponse -Response $response -RequestIndex $Attempt
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
    $httpClient.Timeout = [TimeSpan]::FromSeconds($requestTimeoutSeconds)
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
            $advertisedModels = @($modelsPayload.data | Where-Object { $_.id } | ForEach-Object { [string]$_.id })
            if ($advertisedModels.Count -eq 0) {
                throw "ModelProxy models response did not include usable OpenAI-shaped model ids."
            }
            $defaultModel = @($modelsPayload.data | Where-Object { $_.default -eq $true } | Select-Object -First 1)
            if ($defaultModel.Count -ne 1 -or -not $defaultModel[0].id) {
                throw "ModelProxy models response did not advertise exactly one default model."
            }
            $Model = [string]$defaultModel[0].id
            $catalogState = if ($modelsPayload.modelproxy.catalog_state) { [string]$modelsPayload.modelproxy.catalog_state } else { "unknown" }
            Write-Host "Meeting Recorder ModelProxy default model: $Model (catalog=$catalogState)"
        }
        finally {
            if ($modelsResponse) {
                $modelsResponse.Dispose()
            }

            $modelsRequest.Dispose()
        }
    }

    $result = $null
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        if ($attempt -gt 1) {
            Write-Host "ModelProxy reported retryable backend_busy for synthetic validation; retrying shortly."
            Start-Sleep -Milliseconds (250 * ($attempt - 1))
        }

        $result = Invoke-ModelProxySmokeRetry -Client $httpClient -ModelName $Model -Attempt $attempt
        if (-not $result.RetryableBackendBusy) {
            break
        }
    }

    if ($result.RetryableBackendBusy) {
        throw "ModelProxy remained temporarily saturated after retry attempts; retry shortly."
    }

    if (-not $result.Success) {
        throw "ModelProxy smoke failed for synthetic request with structured error '$($result.ErrorKind)'."
    }

    Write-Host "Meeting Recorder ModelProxy synthetic smoke response matched expected marker for a lightweight no-search codex-routed request."

    $printedRouting = $false
    foreach ($headerName in $result.Routing.Keys) {
        Write-Host "ModelProxy routing: $headerName=$($result.Routing[$headerName])"
        $printedRouting = $true
    }

    if (-not $printedRouting) {
        Write-Host "ModelProxy routing: no optional routing headers were returned."
    }
}
finally {
    $httpClient.Dispose()
}
