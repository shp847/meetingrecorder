param(
    [string]$InstallRoot = "",
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenuShortcut,
    [switch]$TryTaskbarPin,
    [switch]$NoLaunch,
    [switch]$DisableLaunchOnLogin,
    [string]$InstallChannel = "PortableZip",
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

function Resolve-BundleRoot {
    $nestedBundleRoot = Join-Path $PSScriptRoot "MeetingRecorder"
    if (Test-Path (Join-Path $nestedBundleRoot "MeetingRecorder.App.exe")) {
        return $nestedBundleRoot
    }

    if (Test-Path (Join-Path $PSScriptRoot "MeetingRecorder.App.exe")) {
        return $PSScriptRoot
    }

    throw "Could not find the portable Meeting Recorder bundle next to the installer."
}

function Resolve-DeploymentCliPath {
    param(
        [string]$BundleRoot
    )

    $candidates = @(
        (Join-Path $BundleRoot "AppPlatform.Deployment.Cli.exe"),
        (Join-Path $PSScriptRoot "AppPlatform.Deployment.Cli.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Could not find AppPlatform.Deployment.Cli.exe next to the portable bundle."
}

$script:InstallerLogPath = New-InstallerLogPath -OperationName "install-bundle"
Write-Host "Diagnostic log: $script:InstallerLogPath"

try {
    Write-InstallerLog "Install-MeetingRecorder started."
    $bundleRoot = Resolve-BundleRoot
    Write-InstallerLog "Resolved bundle root '$bundleRoot'."
    $deploymentCliPath = Resolve-DeploymentCliPath -BundleRoot $bundleRoot
    Write-InstallerLog "Resolved deployment CLI path '$deploymentCliPath'."

    $cliArguments = @(
        "install-bundle",
        "--bundle-root",
        $bundleRoot,
        "--log-path",
        $script:InstallerLogPath,
        "--pause-on-error"
    )

    if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
        $cliArguments += @("--install-root", $InstallRoot)
    }

    if (-not [string]::IsNullOrWhiteSpace($InstallChannel)) {
        $cliArguments += @("--install-channel", $InstallChannel)
    }

    if ($NoDesktopShortcut.IsPresent) {
        $cliArguments += "--no-desktop-shortcut"
    }

    if ($NoStartMenuShortcut.IsPresent) {
        $cliArguments += "--no-start-menu-shortcut"
    }

    if ($NoLaunch.IsPresent) {
        $cliArguments += "--no-launch"
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion)) {
        $cliArguments += @("--release-version", $ReleaseVersion)
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleasePublishedAtUtc)) {
        $cliArguments += @("--release-published-at-utc", $ReleasePublishedAtUtc)
    }

    if ($ReleaseAssetSizeBytes -gt 0) {
        $cliArguments += @("--release-asset-size-bytes", $ReleaseAssetSizeBytes.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    }

    Write-InstallerLog "Delegating install execution to AppPlatform.Deployment.Cli."
    Write-Host "Delegating install execution to AppPlatform.Deployment.Cli..."
    & $deploymentCliPath @cliArguments
    $exitCode = $LASTEXITCODE
    Write-InstallerLog "AppPlatform.Deployment.Cli exited with code $exitCode."

    if ($exitCode -ne 0) {
        Write-Host "Diagnostic log: $script:InstallerLogPath"
        Pause-OnInstallerError
        exit $exitCode
    }

    if ($TryTaskbarPin.IsPresent) {
        Write-InstallerLog "Taskbar pinning remains a manual step on restricted Windows builds."
        Write-Host "Taskbar pinning remains a manual step on restricted Windows builds." -ForegroundColor Yellow
    }

    if ($DisableLaunchOnLogin.IsPresent) {
        Write-InstallerLog "Launch-on-login changes are not applied by the thin installer wrapper."
        Write-Host "Launch-on-login changes are not applied by the thin installer wrapper." -ForegroundColor Yellow
    }
}
catch {
    Write-InstallerLog ("Install-MeetingRecorder failed: " + $_.Exception.ToString())
    Write-Error $_
    Write-Host "Diagnostic log: $script:InstallerLogPath"
    Pause-OnInstallerError
    exit 1
}
