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
$progressId = 4201
$tempRoot = Join-Path $env:TEMP ("MeetingRecorder-UpdateApply-" + [Guid]::NewGuid().ToString("N"))
$extractPath = Join-Path $tempRoot "extract"

function Set-UpdateProgress {
    param(
        [int]$PercentComplete,
        [string]$Status
    )

    Write-Progress -Id $progressId -Activity "Updating Meeting Recorder" -Status $Status -PercentComplete $PercentComplete
}

function Complete-UpdateProgress {
    Write-Progress -Id $progressId -Activity "Updating Meeting Recorder" -Completed
}

function Wait-ForSourceProcessExit {
    param(
        [int]$ProcessId
    )

    if ($ProcessId -le 0) {
        return
    }

    try {
        $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            return
        }

        Wait-Process -Id $ProcessId -Timeout 300 -ErrorAction Stop
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

function Invoke-StagedInstaller {
    param(
        [string]$InstallerPath,
        [string]$InstallRoot,
        [string]$ReleaseVersion,
        [string]$ReleasePublishedAtUtc,
        [long]$ReleaseAssetSizeBytes
    )

    $parameters = @{
        InstallRoot = $InstallRoot
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

try {
    if (-not (Test-Path $ZipPath)) {
        throw "The downloaded update package '$ZipPath' could not be found."
    }

    Set-UpdateProgress -PercentComplete 10 -Status "Waiting for the current app process to close..."
    Wait-ForSourceProcessExit -ProcessId $SourceProcessId

    New-Item -ItemType Directory -Force -Path $extractPath | Out-Null

    Set-UpdateProgress -PercentComplete 45 -Status "Extracting the downloaded update package..."
    Expand-Archive -Path $ZipPath -DestinationPath $extractPath -Force

    $installerPath = Join-Path $extractPath "Install-MeetingRecorder.ps1"
    if (-not (Test-Path $installerPath)) {
        throw "The downloaded update package did not contain Install-MeetingRecorder.ps1."
    }

    Set-UpdateProgress -PercentComplete 80 -Status "Installing the updated app files..."
    Invoke-StagedInstaller `
        -InstallerPath $installerPath `
        -InstallRoot $InstallRoot `
        -ReleaseVersion $ReleaseVersion `
        -ReleasePublishedAtUtc $ReleasePublishedAtUtc `
        -ReleaseAssetSizeBytes $ReleaseAssetSizeBytes

    Set-UpdateProgress -PercentComplete 100 -Status "Update complete."
}
finally {
    Complete-UpdateProgress
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
    Remove-Item -Path $ZipPath -Force -ErrorAction SilentlyContinue
}
