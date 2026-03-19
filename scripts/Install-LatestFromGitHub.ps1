param(
    [string]$InstallRoot = "",
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenuShortcut,
    [switch]$NoLaunch,
    [switch]$DisableLaunchOnLogin,
    [string]$FeedUrl = "https://api.github.com/repos/shp847/meetingrecorder/releases/latest"
)

$ErrorActionPreference = "Stop"

$script:InstallProgressId = 2871

function Set-InstallProgressPhase {
    param(
        [int]$PercentComplete,
        [string]$Status
    )

    Write-Progress -Id $script:InstallProgressId -Activity "Installing Meeting Recorder" -Status $Status -PercentComplete $PercentComplete
}

function Complete-InstallProgressPhase {
    Write-Progress -Id $script:InstallProgressId -Activity "Installing Meeting Recorder" -Completed
}

function Format-ByteSize {
    param(
        [double]$ByteCount
    )

    $units = @("B", "KB", "MB", "GB", "TB")
    $value = $ByteCount
    $unitIndex = 0

    while ($value -ge 1024 -and $unitIndex -lt ($units.Length - 1)) {
        $value = $value / 1024
        $unitIndex += 1
    }

    if ($unitIndex -eq 0) {
        return ("{0:0} {1}" -f $value, $units[$unitIndex])
    }

    return ("{0:0.0} {1}" -f $value, $units[$unitIndex])
}

function Format-RemainingTime {
    param(
        [TimeSpan]$Remaining
    )

    if ($Remaining.TotalHours -ge 1) {
        return ("{0:0}h {1:00}m" -f [Math]::Floor($Remaining.TotalHours), $Remaining.Minutes)
    }

    if ($Remaining.TotalMinutes -ge 1) {
        return ("{0:0}m {1:00}s" -f [Math]::Floor($Remaining.TotalMinutes), $Remaining.Seconds)
    }

    return ("{0:0}s" -f [Math]::Ceiling([Math]::Max($Remaining.TotalSeconds, 0)))
}

function Enable-BootstrapTls {
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    }
    catch {
        # Keep going if the platform does not expose TLS flags the same way.
    }

    try {
        [Net.ServicePointManager]::CheckCertificateRevocationList = $false
    }
    catch {
        # Keep going if revocation settings are not exposed on this platform.
    }
}

function Get-CurlPath {
    $curl = Get-Command "curl.exe" -ErrorAction SilentlyContinue
    if ($null -ne $curl) {
        return $curl.Source
    }

    return $null
}

function New-HttpClient {
    Enable-BootstrapTls

    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $true
    $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate

    try {
        $handler.CheckCertificateRevocationList = $false
    }
    catch {
        # Keep going if revocation settings are not exposed on this platform.
    }

    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromMinutes(30)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd("MeetingRecorderBootstrap/0.1")

    return [PSCustomObject]@{
        Client = $client
        Handler = $handler
    }
}

function Invoke-TextDownload {
    param(
        [string]$Url
    )

    try {
        $clientBundle = New-HttpClient
        try {
            $response = $clientBundle.Client.GetAsync($Url).GetAwaiter().GetResult()
            $response.EnsureSuccessStatusCode()
            return $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        }
        finally {
            if ($null -ne $response) {
                $response.Dispose()
            }

            $clientBundle.Client.Dispose()
            $clientBundle.Handler.Dispose()
        }
    }
    catch {
        $curlPath = Get-CurlPath
        if ([string]::IsNullOrWhiteSpace($curlPath)) {
            throw
        }

        $tempFile = Join-Path $env:TEMP ("MeetingRecorder-BootstrapText-" + [Guid]::NewGuid().ToString("N") + ".txt")
        try {
            & $curlPath "-L" "--fail" "--silent" "--show-error" "--ssl-no-revoke" "--output" $tempFile $Url
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tempFile)) {
                throw
            }

            return Get-Content -Path $tempFile -Raw
        }
        finally {
            Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-FileDownload {
    param(
        [string]$Url,
        [string]$DestinationPath,
        [string]$Activity = "Installing Meeting Recorder",
        [string]$StatusPrefix = "Downloading"
    )

    try {
        $clientBundle = New-HttpClient
        $response = $null
        $responseStream = $null
        $outputStream = $null

        try {
            $response = $clientBundle.Client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            $response.EnsureSuccessStatusCode()

            $contentLength = $response.Content.Headers.ContentLength
            $responseStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
            $outputDirectory = Split-Path -Parent $DestinationPath
            if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
                New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
            }

            $outputStream = [System.IO.File]::Open($DestinationPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)

            $buffer = New-Object byte[] 65536
            $bytesDownloaded = 0L
            $downloadTimer = [System.Diagnostics.Stopwatch]::StartNew()
            $lastProgressUpdateMs = -500

            while (($bytesRead = $responseStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $outputStream.Write($buffer, 0, $bytesRead)
                $bytesDownloaded += $bytesRead

                if (($downloadTimer.ElapsedMilliseconds - $lastProgressUpdateMs) -ge 200) {
                    $status = if ($contentLength -gt 0) {
                        $percentComplete = [Math]::Min([Math]::Floor(($bytesDownloaded * 100.0) / $contentLength), 99)
                        $secondsElapsed = [Math]::Max($downloadTimer.Elapsed.TotalSeconds, 0.1)
                        $bytesPerSecond = $bytesDownloaded / $secondsElapsed
                        $remainingBytes = [Math]::Max($contentLength - $bytesDownloaded, 0)
                        $remainingTime = if ($bytesPerSecond -gt 0) {
                            [TimeSpan]::FromSeconds($remainingBytes / $bytesPerSecond)
                        }
                        else {
                            [TimeSpan]::Zero
                        }

                        Write-Progress -Id $script:InstallProgressId -Activity $Activity -Status ("{0} {1}% ({2} of {3}, ~{4} remaining)" -f $StatusPrefix, $percentComplete, (Format-ByteSize -ByteCount $bytesDownloaded), (Format-ByteSize -ByteCount $contentLength), (Format-RemainingTime -Remaining $remainingTime)) -PercentComplete $percentComplete
                    }
                    else {
                        Write-Progress -Id $script:InstallProgressId -Activity $Activity -Status ("{0} {1} downloaded" -f $StatusPrefix, (Format-ByteSize -ByteCount $bytesDownloaded)) -PercentComplete -1
                    }

                    $lastProgressUpdateMs = $downloadTimer.ElapsedMilliseconds
                }
            }

            if ($contentLength -gt 0) {
                Write-Progress -Id $script:InstallProgressId -Activity $Activity -Status ("{0} 100% ({1})" -f $StatusPrefix, (Format-ByteSize -ByteCount $bytesDownloaded)) -PercentComplete 100
            }
            else {
                Write-Progress -Id $script:InstallProgressId -Activity $Activity -Status ("{0} complete ({1})" -f $StatusPrefix, (Format-ByteSize -ByteCount $bytesDownloaded)) -PercentComplete 100
            }

            return
        }
        finally {
            if ($null -ne $outputStream) {
                $outputStream.Dispose()
            }

            if ($null -ne $responseStream) {
                $responseStream.Dispose()
            }

            if ($null -ne $response) {
                $response.Dispose()
            }

            $clientBundle.Client.Dispose()
            $clientBundle.Handler.Dispose()
        }
    }
    catch {
        $curlPath = Get-CurlPath
        if ([string]::IsNullOrWhiteSpace($curlPath)) {
            throw
        }

        & $curlPath "-L" "--fail" "--show-error" "--progress-bar" "--ssl-no-revoke" "--output" $DestinationPath $Url
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $DestinationPath)) {
            throw
        }
    }
}

function Get-LatestRelease {
    param(
        [string]$Url
    )

    try {
        $payload = Invoke-TextDownload -Url $Url
        return $payload | ConvertFrom-Json
    }
    catch {
        $statusCode = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode.value__ } else { -1 }
        if ($statusCode -eq 404) {
            throw "No published GitHub release was found at '$Url'. Publish a GitHub Release first."
        }

        throw
    }
}

function Normalize-ReleaseVersion {
    param(
        [string]$RawVersion
    )

    if ([string]::IsNullOrWhiteSpace($RawVersion)) {
        return ""
    }

    if ($RawVersion.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        return $RawVersion.Substring(1)
    }

    return $RawVersion
}

function Get-PreferredReleaseAsset {
    param(
        $Release
    )

    $assets = @($Release.assets)
    $preferred = $assets |
        Where-Object {
            $_.browser_download_url -and
            $_.name -and
            $_.name -match "win-x64" -and
            $_.name.EndsWith(".zip", [StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1

    if ($null -ne $preferred) {
        return $preferred
    }

    $fallback = $assets |
        Where-Object {
            $_.browser_download_url -and
            $_.name -and
            $_.name.EndsWith(".zip", [StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1

    if ($null -ne $fallback) {
        return $fallback
    }

    throw "The latest GitHub release does not contain a ZIP installer asset."
}

function Invoke-StagedInstaller {
    param(
        [string]$InstallerPath,
        [string]$InstallRoot,
        [switch]$NoDesktopShortcut,
        [switch]$NoStartMenuShortcut,
        [switch]$NoLaunch,
        [switch]$DisableLaunchOnLogin,
        [string]$ReleaseVersion,
        [string]$ReleasePublishedAtUtc,
        [long]$ReleaseAssetSizeBytes
    )

    $parameters = @{}
    if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
        $parameters.InstallRoot = $InstallRoot
    }

    if ($NoDesktopShortcut.IsPresent) {
        $parameters.NoDesktopShortcut = $true
    }

    if ($NoStartMenuShortcut.IsPresent) {
        $parameters.NoStartMenuShortcut = $true
    }

    if ($NoLaunch.IsPresent) {
        $parameters.NoLaunch = $true
    }

    if ($DisableLaunchOnLogin.IsPresent) {
        $parameters.DisableLaunchOnLogin = $true
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion)) {
        $parameters.ReleaseVersion = $ReleaseVersion
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleasePublishedAtUtc)) {
        $parameters.ReleasePublishedAtUtc = $ReleasePublishedAtUtc
    }

    if ($ReleaseAssetSizeBytes -gt 0) {
        $parameters.ReleaseAssetSizeBytes = $ReleaseAssetSizeBytes
    }

    & $InstallerPath @parameters
}

$tempRoot = Join-Path $env:TEMP ("MeetingRecorder-GitHubInstall-" + [Guid]::NewGuid().ToString("N"))
$zipPath = Join-Path $tempRoot "MeetingRecorder.zip"
$extractPath = Join-Path $tempRoot "extract"

try {
    Set-InstallProgressPhase -PercentComplete 5 -Status "Checking the latest GitHub release..."
    $release = Get-LatestRelease -Url $FeedUrl
    $releaseAsset = Get-PreferredReleaseAsset -Release $release
    $downloadUrl = [string]$releaseAsset.browser_download_url
    $versionLabel = if ($release.tag_name) { [string]$release.tag_name } else { "latest" }
    $normalizedVersion = Normalize-ReleaseVersion -RawVersion $versionLabel
    $releaseDisplayName = if (-not [string]::IsNullOrWhiteSpace([string]$release.name)) {
        [string]$release.name
    }
    elseif (-not [string]::IsNullOrWhiteSpace($normalizedVersion)) {
        "Meeting Recorder v$normalizedVersion"
    }
    else {
        "Meeting Recorder"
    }
    $publishedAtUtc = if ($release.published_at) { [string]$release.published_at } else { "" }
    $assetSizeBytes = if ($releaseAsset.size) { [long]$releaseAsset.size } else { 0 }

    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $extractPath | Out-Null

    Write-Host "Downloading $releaseDisplayName from GitHub..."
    Invoke-FileDownload -Url $downloadUrl -DestinationPath $zipPath -Activity "Installing Meeting Recorder" -StatusPrefix "Downloading installer bundle"

    Set-InstallProgressPhase -PercentComplete 75 -Status "Extracting installer bundle..."
    Write-Host "Extracting installer bundle..."
    Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

    $installerPath = Join-Path $extractPath "Install-MeetingRecorder.ps1"
    if (-not (Test-Path $installerPath)) {
        throw "The downloaded release did not contain Install-MeetingRecorder.ps1."
    }

    Set-InstallProgressPhase -PercentComplete 90 -Status "Running the bundled installer..."
    Write-Host "Running the bundled installer..."
    Invoke-StagedInstaller `
        -InstallerPath $installerPath `
        -InstallRoot $InstallRoot `
        -NoDesktopShortcut:$NoDesktopShortcut `
        -NoStartMenuShortcut:$NoStartMenuShortcut `
        -NoLaunch:$NoLaunch `
        -DisableLaunchOnLogin:$DisableLaunchOnLogin `
        -ReleaseVersion $normalizedVersion `
        -ReleasePublishedAtUtc $publishedAtUtc `
        -ReleaseAssetSizeBytes $assetSizeBytes

    Set-InstallProgressPhase -PercentComplete 100 -Status "Install complete."
}
catch {
    $message = $_.Exception.Message
    Write-Host ""
    Write-Host $message -ForegroundColor Red

    if ($message -match "still running|currently running") {
        Write-Host "Close Meeting Recorder and any background processing, then run the installer again." -ForegroundColor Yellow
    }

    exit 1
}
finally {
    Complete-InstallProgressPhase
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
