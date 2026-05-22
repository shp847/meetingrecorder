param(
    [string]$BaseUrl = "http://127.0.0.1:8645/v1",
    [string]$ApiKey = $env:MODELPROXY_MEETING_RECORDER_API_KEY,
    [string]$Model = "gpt-5.4-mini"
)

$ErrorActionPreference = "Stop"

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

$response = Invoke-RestMethod `
    -Method Post `
    -Uri "$($BaseUrl.TrimEnd('/'))/chat/completions" `
    -Headers $headers `
    -Body $body `
    -TimeoutSec 900

$content = $response.choices[0].message.content
Write-Host "Meeting Recorder ModelProxy synthetic smoke response: $content"
if ($content -notmatch "summary-provider-ok") {
    throw "Unexpected ModelProxy response."
}
