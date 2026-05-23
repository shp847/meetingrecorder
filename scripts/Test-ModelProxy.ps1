param(
    [string]$BaseUrl = "http://127.0.0.1:8645/v1",
    [string]$ApiKey = $env:MODELPROXY_MEETING_RECORDER_API_KEY,
    [string]$Model = $null
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Net.Http

$normalizedBaseUrl = $BaseUrl.TrimEnd("/")

if (-not $ApiKey) {
    $ApiKey = "sk-modelproxy"
}

$httpClient = [System.Net.Http.HttpClient]::new()
$httpRequest = $null
$httpResponse = $null
try {
    $httpClient.Timeout = [TimeSpan]::FromSeconds(900)
    if (-not $Model) {
        $modelsRequest = [System.Net.Http.HttpRequestMessage]::new(
            [System.Net.Http.HttpMethod]::Get,
            "$normalizedBaseUrl/models")
        $modelsRequest.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $ApiKey)
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

    $body = @{
        model = $Model
        messages = @(@{ role = "user"; content = "Reply exactly: summary-provider-ok" })
    } | ConvertTo-Json -Depth 8

    $httpRequest = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Post,
        "$normalizedBaseUrl/chat/completions")
    $httpRequest.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $ApiKey)
    $httpRequest.Headers.TryAddWithoutValidation("X-ModelProxy-Web-Search", "false") | Out-Null
    $httpRequest.Content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, "application/json")

    $httpResponse = $httpClient.SendAsync($httpRequest).GetAwaiter().GetResult()
    $responseContent = $httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $httpResponse.IsSuccessStatusCode) {
        throw "ModelProxy smoke failed with HTTP $([int]$httpResponse.StatusCode) $($httpResponse.ReasonPhrase)."
    }

    $payload = $responseContent | ConvertFrom-Json
    $content = $payload.choices[0].message.content
    Write-Host "Meeting Recorder ModelProxy synthetic smoke response: $content"

    $routingHeaderNames = @(
        "X-ModelProxy-Request-Id",
        "X-ModelProxy-Requested-Backend",
        "X-ModelProxy-Effective-Backend",
        "X-ModelProxy-Web-Search-Backend",
        "X-ModelProxy-App-Server-Web-Search-Supported",
        "X-ModelProxy-Fallback-Reason"
    )
    foreach ($headerName in $routingHeaderNames) {
        if ($httpResponse.Headers.Contains($headerName)) {
            $headerValue = [string]::Join(",", $httpResponse.Headers.GetValues($headerName))
            if ($headerValue) {
                Write-Host "ModelProxy routing: $headerName=$headerValue"
            }
        }
    }

    if ($content -notmatch "summary-provider-ok") {
        throw "Unexpected ModelProxy response."
    }
}
finally {
    if ($httpResponse) {
        $httpResponse.Dispose()
    }

    if ($httpRequest) {
        $httpRequest.Dispose()
    }

    $httpClient.Dispose()
}
