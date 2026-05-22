param(
    [string]$BaseUrl = "http://127.0.0.1:8645/v1",
    [string]$ApiKey = $env:MODELPROXY_MEETING_RECORDER_API_KEY,
    [string]$Model = "gpt-5.4-mini"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Net.Http

if (-not $ApiKey) {
    $ApiKey = "sk-modelproxy"
}

$headers = @{
    Authorization = "Bearer $ApiKey"
    "Content-Type" = "application/json"
    "X-ModelProxy-Web-Search" = "false"
}
$body = @{
    model = $Model
    messages = @(@{ role = "user"; content = "Reply exactly: summary-provider-ok" })
} | ConvertTo-Json -Depth 8

$httpClient = [System.Net.Http.HttpClient]::new()
$httpRequest = $null
$httpResponse = $null
try {
    $httpClient.Timeout = [TimeSpan]::FromSeconds(900)
    $httpRequest = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Post,
        "$($BaseUrl.TrimEnd('/'))/chat/completions")
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
