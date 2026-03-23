param(
    [Parameter(Mandatory = $true)]
    [string]$ZipPath,
    [Parameter(Mandatory = $true)]
    [string]$InstallRoot,
    [int]$SourceProcessId = 0,
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
    [void](Read-Host "The updater reported an error. Review the messages above, then press Enter to close this window")
}

$script:InstallerLogPath = New-InstallerLogPath -OperationName "apply-update"
Write-Host "Diagnostic log: $script:InstallerLogPath"

try {
    Write-InstallerLog "Apply-DownloadedUpdate started."
    $deploymentCliPath = Join-Path $PSScriptRoot "AppPlatform.Deployment.Cli.exe"
    if (-not (Test-Path $deploymentCliPath)) {
        throw "AppPlatform.Deployment.Cli.exe is missing from the installed app folder."
    }

    Write-InstallerLog "Resolved deployment CLI path '$deploymentCliPath'."

    $cliArguments = @(
        "apply-update",
        "--zip-path",
        $ZipPath,
        "--install-root",
        $InstallRoot,
        "--source-process-id",
        $SourceProcessId.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        "--update-channel",
        "AutoUpdate",
        "--log-path",
        $script:InstallerLogPath,
        "--pause-on-error"
    )

    if (-not [string]::IsNullOrWhiteSpace($ReleaseVersion)) {
        $cliArguments += @("--release-version", $ReleaseVersion)
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleasePublishedAtUtc)) {
        $cliArguments += @("--release-published-at-utc", $ReleasePublishedAtUtc)
    }

    if ($ReleaseAssetSizeBytes -gt 0) {
        $cliArguments += @("--release-asset-size-bytes", $ReleaseAssetSizeBytes.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    }

    Write-InstallerLog "Delegating update apply execution to AppPlatform.Deployment.Cli."
    Write-Host "Delegating update apply execution to AppPlatform.Deployment.Cli..."
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
    Write-InstallerLog ("Apply-DownloadedUpdate failed: " + $_.Exception.ToString())
    Write-Error $_
    Write-Host "Diagnostic log: $script:InstallerLogPath"
    Pause-OnInstallerError
    exit 1
}
