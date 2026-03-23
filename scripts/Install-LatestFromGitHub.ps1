param(
    [string]$InstallRoot = "",
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenuShortcut,
    [switch]$NoLaunch,
    [switch]$DisableLaunchOnLogin,
    [string]$FeedUrl = "https://api.github.com/repos/shp847/meetingrecorder/releases/latest",
    [string]$InstallChannel = "CommandBootstrap",
    [string]$PackageZipPath = "",
    [string]$ReleaseVersion = "",
    [string]$ReleasePublishedAtUtc = "",
    [long]$ReleaseAssetSizeBytes = 0
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function New-InstallerLogPath {
    param(
        [string]$OperationName
    )

    $logDirectory = Join-Path $env:TEMP "MeetingRecorderInstaller"
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    return Join-Path $logDirectory ("{0}-{1}.log" -f $OperationName, (Get-Date -Format "yyyyMMdd-HHmmss"))
}

function Write-InstallerLog {
    param(
        [string]$Message
    )

    $timestamp = [DateTimeOffset]::Now.ToString("O")
    Add-Content -Path $script:InstallerLogPath -Value "$timestamp $Message" -Encoding UTF8
}

function Pause-OnInstallerError {
    if ($env:MEETINGRECORDER_SKIP_SCRIPT_PAUSE -eq "1") {
        return
    }

    Write-Host ""
    [void](Read-Host "The installer reported an error. Review the messages above, then press Enter to close this window")
}

function Resolve-LocalDeploymentCliPath {
    $candidates = @(
        (Join-Path $PSScriptRoot "AppPlatform.Deployment.Cli.exe"),
        (Join-Path $PSScriptRoot "MeetingRecorder\AppPlatform.Deployment.Cli.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Resolve-LocalPackageZipPath {
    if (-not [string]::IsNullOrWhiteSpace($PackageZipPath)) {
        if ([System.IO.Path]::IsPathRooted($PackageZipPath)) {
            return [System.IO.Path]::GetFullPath($PackageZipPath)
        }

        return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $PackageZipPath))
    }

    $localZip = Get-ChildItem -Path $PSScriptRoot -Filter "MeetingRecorder-*.zip" -File -ErrorAction SilentlyContinue |
        Sort-Object @{ Expression = { $_.Name -match "win-x64" }; Descending = $true }, @{ Expression = { $_.LastWriteTimeUtc }; Descending = $true } |
        Select-Object -First 1

    if ($null -eq $localZip) {
        return $null
    }

    return $localZip.FullName
}

function Get-LatestRelease {
    param(
        [string]$Url
    )

    return Invoke-RestMethod -Headers @{ "User-Agent" = "AppPlatform.Deployment.Cli bootstrap" } -Uri $Url -Method Get
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

    return $assets |
        Where-Object {
            $_.browser_download_url -and
            $_.name -and
            $_.name.EndsWith(".zip", [StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1
}

function Resolve-DeploymentCliPathFromExtractedBundle {
    param(
        [string]$ExtractRoot
    )

    $candidates = @(
        (Join-Path $ExtractRoot "AppPlatform.Deployment.Cli.exe"),
        (Join-Path $ExtractRoot "MeetingRecorder\AppPlatform.Deployment.Cli.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "The downloaded release did not contain AppPlatform.Deployment.Cli.exe."
}

function Build-CliArguments {
    param(
        [string]$BundleRoot,
        [string]$InstallRoot,
        [string]$InstallChannel,
        [string]$ReleaseVersion,
        [string]$ReleasePublishedAtUtc,
        [long]$ReleaseAssetSizeBytes
    )

    $arguments = @(
        "install-bundle",
        "--bundle-root",
        $BundleRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
        $arguments += @("--install-root", $InstallRoot)
    }

    if (-not [string]::IsNullOrWhiteSpace($InstallChannel)) {
        $arguments += @("--install-channel", $InstallChannel)
    }

    if ($NoDesktopShortcut.IsPresent) {
        $arguments += "--no-desktop-shortcut"
    }

    if ($NoStartMenuShortcut.IsPresent) {
        $arguments += "--no-start-menu-shortcut"
    }

    if ($NoLaunch.IsPresent) {
        $arguments += "--no-launch"
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion)) {
        $arguments += @("--release-version", $ReleaseVersion)
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleasePublishedAtUtc)) {
        $arguments += @("--release-published-at-utc", $ReleasePublishedAtUtc)
    }

    if ($ReleaseAssetSizeBytes -gt 0) {
        $arguments += @("--release-asset-size-bytes", $ReleaseAssetSizeBytes.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    }

    return $arguments
}

$script:InstallerLogPath = New-InstallerLogPath -OperationName "install-latest"
Write-Host "Diagnostic log: $script:InstallerLogPath"

$tempRoot = Join-Path $env:TEMP ("MeetingRecorder-GitHubInstall-" + [Guid]::NewGuid().ToString("N"))
$zipPath = Join-Path $tempRoot "MeetingRecorder.zip"
$extractPath = Join-Path $tempRoot "extract"

try {
    Write-InstallerLog "Install-LatestFromGitHub started."
    $resolvedPackageZipPath = Resolve-LocalPackageZipPath
    if (-not [string]::IsNullOrWhiteSpace($resolvedPackageZipPath)) {
        if (-not (Test-Path $resolvedPackageZipPath)) {
            throw "The provided package ZIP '$resolvedPackageZipPath' does not exist."
        }

        Write-InstallerLog ("Using local package zip '{0}'." -f $resolvedPackageZipPath)
        Write-Host "Using local package zip..."

        New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
        New-Item -ItemType Directory -Force -Path $extractPath | Out-Null

        Expand-Archive -Path $resolvedPackageZipPath -DestinationPath $extractPath -Force

        $deploymentCliPath = Resolve-DeploymentCliPathFromExtractedBundle -ExtractRoot $extractPath
        $bundleRoot = Split-Path -Parent $deploymentCliPath

        if ([string]::IsNullOrWhiteSpace($ReleasePublishedAtUtc)) {
            $ReleasePublishedAtUtc = (Get-Item -Path $resolvedPackageZipPath).LastWriteTimeUtc.ToString("O")
        }

        if ($ReleaseAssetSizeBytes -le 0) {
            $ReleaseAssetSizeBytes = [long](Get-Item -Path $resolvedPackageZipPath).Length
        }

        $cliArguments = Build-CliArguments `
            -BundleRoot $bundleRoot `
            -InstallRoot $InstallRoot `
            -InstallChannel $InstallChannel `
            -ReleaseVersion $ReleaseVersion `
            -ReleasePublishedAtUtc $ReleasePublishedAtUtc `
            -ReleaseAssetSizeBytes $ReleaseAssetSizeBytes
        $cliArguments += @("--log-path", $script:InstallerLogPath)
        $cliArguments += "--pause-on-error"

        Write-InstallerLog ("Delegating local package install execution to '{0}'." -f $deploymentCliPath)
        Write-Host "Delegating local package install execution to AppPlatform.Deployment.Cli..."
        & $deploymentCliPath @cliArguments
        $exitCode = $LASTEXITCODE
        Write-InstallerLog "AppPlatform.Deployment.Cli exited with code $exitCode."
        if ($exitCode -ne 0) {
            Write-Host "Diagnostic log: $script:InstallerLogPath"
            Pause-OnInstallerError
        }

        exit $exitCode
    }

    $localDeploymentCliPath = Resolve-LocalDeploymentCliPath
    if ($null -ne $localDeploymentCliPath) {
        Write-InstallerLog "Found local deployment CLI at '$localDeploymentCliPath'."
        $localArguments = @(
            "install-latest",
            "--log-path",
            $script:InstallerLogPath,
            "--pause-on-error"
        )
        if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
            $localArguments += @("--install-root", $InstallRoot)
        }

        if ($NoDesktopShortcut.IsPresent) {
            $localArguments += "--no-desktop-shortcut"
        }

        if ($NoStartMenuShortcut.IsPresent) {
            $localArguments += "--no-start-menu-shortcut"
        }

        if ($NoLaunch.IsPresent) {
            $localArguments += "--no-launch"
        }

        if (-not [string]::IsNullOrWhiteSpace($FeedUrl)) {
            $localArguments += @("--feed-url", $FeedUrl)
        }

        if (-not [string]::IsNullOrWhiteSpace($InstallChannel)) {
            $localArguments += @("--install-channel", $InstallChannel)
        }

        Write-InstallerLog "Delegating latest-release install execution to AppPlatform.Deployment.Cli."
        Write-Host "Delegating latest-release install execution to AppPlatform.Deployment.Cli..."
        & $localDeploymentCliPath @localArguments
        $exitCode = $LASTEXITCODE
        Write-InstallerLog "AppPlatform.Deployment.Cli exited with code $exitCode."
        if ($exitCode -ne 0) {
            Write-Host "Diagnostic log: $script:InstallerLogPath"
            Pause-OnInstallerError
        }

        exit $exitCode
    }

    Write-InstallerLog "No local CLI was found; downloading release metadata and bundle."
    $release = Get-LatestRelease -Url $FeedUrl
    $releaseAsset = Get-PreferredReleaseAsset -Release $release
    if ($null -eq $releaseAsset) {
        throw "The latest GitHub release does not contain a ZIP installer asset."
    }

    Write-InstallerLog ("Selected release asset '{0}'." -f [string]$releaseAsset.name)

    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $extractPath | Out-Null

    Write-InstallerLog ("Downloading release asset from '{0}' to '{1}'." -f [string]$releaseAsset.browser_download_url, $zipPath)
    Invoke-WebRequest `
        -UseBasicParsing `
        -Headers @{ "User-Agent" = "AppPlatform.Deployment.Cli bootstrap" } `
        -Uri ([string]$releaseAsset.browser_download_url) `
        -OutFile $zipPath

    Write-InstallerLog ("Extracting downloaded archive '{0}' into '{1}'." -f $zipPath, $extractPath)
    Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

    $deploymentCliPath = Resolve-DeploymentCliPathFromExtractedBundle -ExtractRoot $extractPath
    $bundleRoot = Split-Path -Parent $deploymentCliPath
    $normalizedVersion = [string]$release.tag_name
    if ($normalizedVersion.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        $normalizedVersion = $normalizedVersion.Substring(1)
    }

    $releasePublishedAtUtc = if ($release.published_at) { [string]$release.published_at } else { "" }
    $releaseAssetSizeBytes = if ($releaseAsset.size) { [long]$releaseAsset.size } else { 0 }

    $cliArguments = Build-CliArguments `
        -BundleRoot $bundleRoot `
        -InstallRoot $InstallRoot `
        -InstallChannel $InstallChannel `
        -ReleaseVersion $normalizedVersion `
        -ReleasePublishedAtUtc $releasePublishedAtUtc `
        -ReleaseAssetSizeBytes $releaseAssetSizeBytes
    $cliArguments += @("--log-path", $script:InstallerLogPath)
    $cliArguments += "--pause-on-error"

    Write-InstallerLog ("Delegating downloaded bundle install execution to '{0}'." -f $deploymentCliPath)
    Write-Host "Delegating downloaded bundle install execution to AppPlatform.Deployment.Cli..."
    & $deploymentCliPath @cliArguments
    $exitCode = $LASTEXITCODE
    Write-InstallerLog "AppPlatform.Deployment.Cli exited with code $exitCode."
    if ($exitCode -ne 0) {
        Write-Host "Diagnostic log: $script:InstallerLogPath"
        Pause-OnInstallerError
    }

    exit $exitCode
}
catch {
    Write-InstallerLog ("Install-LatestFromGitHub failed: " + $_.Exception.ToString())
    Write-Error $_
    Write-Host "Diagnostic log: $script:InstallerLogPath"
    Pause-OnInstallerError
    exit 1
}
finally {
    Write-InstallerLog ("Cleaning up temporary workspace '{0}'." -f $tempRoot)
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
