param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = ".artifacts\publish\win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot $OutputRoot
$appTemp = Join-Path $outputPath "app-temp"
$workerTemp = Join-Path $outputPath "worker-temp"
$finalPath = Join-Path $outputPath "MeetingRecorder"
$appAssetPath = Join-Path $repoRoot "src\MeetingRecorder.App\Assets\MeetingRecorder.ico"
$selfContained = -not $FrameworkDependent.IsPresent
$selfContainedValue = if ($selfContained) { "true" } else { "false" }
$bundleMode = if ($selfContained) { "self-contained" } else { "framework-dependent" }

function Invoke-DotnetPublish {
    param(
        [string]$ProjectPath,
        [string]$PublishOutput
    )

    $restoreArgs = @(
        "restore",
        $ProjectPath,
        "-p:RestoreIgnoreFailedSources=true",
        "-p:NuGetAudit=false"
    )

    if ($selfContained) {
        $restoreArgs += @("-r", $Runtime)
    }

    & dotnet @restoreArgs

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed for $ProjectPath"
    }

    $publishArgs = @(
        "publish",
        $ProjectPath,
        "-c",
        $Configuration,
        "--force",
        "--self-contained",
        $selfContainedValue,
        "-p:RestoreIgnoreFailedSources=true",
        "-p:NuGetAudit=false",
        "-o",
        $PublishOutput
    )

    if ($selfContained) {
        $publishArgs += @("-r", $Runtime)
    }

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ProjectPath"
    }
}

Remove-Item -Recurse -Force $appTemp -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $workerTemp -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $finalPath -ErrorAction SilentlyContinue

Invoke-DotnetPublish -ProjectPath (Join-Path $repoRoot "src\MeetingRecorder.App\MeetingRecorder.App.csproj") -PublishOutput $appTemp
Invoke-DotnetPublish -ProjectPath (Join-Path $repoRoot "src\MeetingRecorder.ProcessingWorker\MeetingRecorder.ProcessingWorker.csproj") -PublishOutput $workerTemp

New-Item -ItemType Directory -Force -Path $finalPath | Out-Null
Copy-Item -Path (Join-Path $appTemp "*") -Destination $finalPath -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Run-MeetingRecorder.cmd") -Destination (Join-Path $finalPath "Run-MeetingRecorder.cmd") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Apply-DownloadedUpdate.ps1") -Destination (Join-Path $finalPath "Apply-DownloadedUpdate.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-LatestFromGitHub.ps1") -Destination (Join-Path $finalPath "Install-LatestFromGitHub.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-LatestFromGitHub.cmd") -Destination (Join-Path $finalPath "Install-LatestFromGitHub.cmd") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Check-Dependencies.ps1") -Destination (Join-Path $finalPath "Check-Dependencies.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-Dependencies.ps1") -Destination (Join-Path $finalPath "Install-Dependencies.ps1") -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-Dependencies.cmd") -Destination (Join-Path $finalPath "Install-Dependencies.cmd") -Force
Copy-Item -Path (Join-Path $repoRoot "SETUP.md") -Destination (Join-Path $finalPath "SETUP.md") -Force
Set-Content -Path (Join-Path $finalPath "portable.mode") -Value "portable" -NoNewline
Set-Content -Path (Join-Path $finalPath "bundle-mode.txt") -Value $bundleMode -NoNewline

if (Test-Path $appAssetPath) {
    Copy-Item -Path $appAssetPath -Destination (Join-Path $finalPath "MeetingRecorder.ico") -Force
}

$workerArtifacts = @(
    "MeetingRecorder.ProcessingWorker.exe",
    "MeetingRecorder.ProcessingWorker.dll",
    "MeetingRecorder.ProcessingWorker.pdb",
    "MeetingRecorder.ProcessingWorker.deps.json",
    "MeetingRecorder.ProcessingWorker.runtimeconfig.json",
    "Whisper.net.dll",
    "Whisper.net.Runtime.dll"
)

foreach ($artifact in $workerArtifacts) {
    $source = Join-Path $workerTemp $artifact
    if (Test-Path $source) {
        Copy-Item -Path $source -Destination $finalPath -Force
    }
}

Get-ChildItem -Path $workerTemp -Filter "runtimes" -Directory | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $finalPath $_.Name) -Recurse -Force
}

Write-Host "Portable publish assembled at $finalPath"
