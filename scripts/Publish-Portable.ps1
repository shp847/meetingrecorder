param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = ".artifacts\publish\win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot $OutputRoot
$appTemp = Join-Path $outputPath "app-temp"
$workerTemp = Join-Path $outputPath "worker-temp"
$finalPath = Join-Path $outputPath "MeetingRecorder"
$selfContainedValue = if ($SelfContained.IsPresent) { "true" } else { "false" }

function Invoke-DotnetPublish {
    param(
        [string]$ProjectPath,
        [string]$PublishOutput
    )

    $publishArgs = @(
        "publish",
        $ProjectPath,
        "-c",
        $Configuration,
        "--self-contained",
        $selfContainedValue,
        "-p:RestoreIgnoreFailedSources=true",
        "-o",
        $PublishOutput
    )

    if ($SelfContained.IsPresent) {
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
Set-Content -Path (Join-Path $finalPath "portable.mode") -Value "portable" -NoNewline

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
