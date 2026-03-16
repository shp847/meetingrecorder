param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PackageRoot = ".artifacts\installer\win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$packagePath = Join-Path $repoRoot $PackageRoot
$stagingPath = Join-Path $packagePath "staging"
$bundleRoot = Join-Path $stagingPath "MeetingRecorder"
$zipPath = Join-Path $packagePath "MeetingRecorder-$Runtime.zip"
$publishScript = Join-Path $PSScriptRoot "Publish-Portable.ps1"
$publishedAppPath = Join-Path $repoRoot ".artifacts\publish\$Runtime\MeetingRecorder"

Remove-Item -Recurse -Force $stagingPath -ErrorAction SilentlyContinue

& $publishScript -Configuration $Configuration -Runtime $Runtime -OutputRoot ".artifacts\publish\$Runtime" -SelfContained:$SelfContained

if ($LASTEXITCODE -ne 0) {
    throw "Portable publish failed while building the installer bundle."
}

New-Item -ItemType Directory -Force -Path $bundleRoot | Out-Null
Copy-Item -Path (Join-Path $publishedAppPath "*") -Destination $bundleRoot -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Run-MeetingRecorder.cmd") -Destination (Join-Path $stagingPath "Run-MeetingRecorder.cmd") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-MeetingRecorder.ps1") -Destination (Join-Path $stagingPath "Install-MeetingRecorder.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-MeetingRecorder.cmd") -Destination (Join-Path $stagingPath "Install-MeetingRecorder.cmd") -Force
Copy-Item -Path (Join-Path $repoRoot "SETUP.md") -Destination (Join-Path $stagingPath "SETUP.md") -Force

if (Test-Path $zipPath) {
    try {
        Remove-Item -Force $zipPath -ErrorAction Stop
    }
    catch {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $zipPath = Join-Path $packagePath "MeetingRecorder-$Runtime-$timestamp.zip"
    }
}

Compress-Archive -Path (Join-Path $stagingPath "*") -DestinationPath $zipPath -Force

Write-Host "Installer bundle assembled at $zipPath"
