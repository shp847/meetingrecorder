param(
    [string]$AppRoot = $PSScriptRoot,
    [switch]$SuppressOptionalWarnings,
    [switch]$QuietSuccess
)

$ErrorActionPreference = "Stop"

$normalizedAppRoot = if ($null -ne $AppRoot) {
    $AppRoot.Trim()
}
else {
    $PSScriptRoot
}
$normalizedAppRoot = $normalizedAppRoot.Trim('"')

if ([string]::IsNullOrWhiteSpace($normalizedAppRoot)) {
    $normalizedAppRoot = $PSScriptRoot
}

$resolvedAppRoot = [System.IO.Path]::GetFullPath($normalizedAppRoot)
$appExePath = Join-Path $resolvedAppRoot "MeetingRecorder.App.exe"
$workerExePath = Join-Path $resolvedAppRoot "MeetingRecorder.ProcessingWorker.exe"
$workerDllPath = Join-Path $resolvedAppRoot "MeetingRecorder.ProcessingWorker.dll"
$workerDepsPath = Join-Path $resolvedAppRoot "MeetingRecorder.ProcessingWorker.deps.json"
$workerRuntimeConfigPath = Join-Path $resolvedAppRoot "MeetingRecorder.ProcessingWorker.runtimeconfig.json"
$workerCorePath = Join-Path $resolvedAppRoot "MeetingRecorder.Core.dll"
$setupPath = Join-Path $resolvedAppRoot "SETUP.md"
$bundleModePath = Join-Path $resolvedAppRoot "bundle-mode.txt"
$modelDirectory = Join-Path $resolvedAppRoot "data\models\asr"

function Get-BundleMode {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return "framework-dependent"
    }

    $mode = (Get-Content $Path | Select-Object -First 1).Trim().ToLowerInvariant()
    if ($mode -eq "self-contained") {
        return $mode
    }

    return "framework-dependent"
}

function Test-WindowsDesktopRuntime {
    try {
        $runtimeLines = & dotnet --list-runtimes 2>$null
        return ($runtimeLines | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App 8\.' } | Select-Object -First 1) -ne $null
    }
    catch {
        return $false
    }
}

function Test-VisualCppRuntime {
    return (Test-Path (Join-Path $env:WINDIR "System32\vcruntime140.dll")) -or
        (Test-Path (Join-Path $env:WINDIR "SysWOW64\vcruntime140.dll"))
}

if (-not (Test-Path $appExePath)) {
    Write-Host "Meeting Recorder cannot start because MeetingRecorder.App.exe is missing from this install: $appExePath"
    Write-Host "Repair or reinstall the app bundle instead of launching the WPF app from MeetingRecorder.App.dll."
    exit 1
}

$missingWorkerPayload = @(
    @{ Name = "MeetingRecorder.ProcessingWorker.exe"; Path = $workerExePath },
    @{ Name = "MeetingRecorder.ProcessingWorker.dll"; Path = $workerDllPath },
    @{ Name = "MeetingRecorder.ProcessingWorker.deps.json"; Path = $workerDepsPath },
    @{ Name = "MeetingRecorder.ProcessingWorker.runtimeconfig.json"; Path = $workerRuntimeConfigPath },
    @{ Name = "MeetingRecorder.Core.dll"; Path = $workerCorePath }
) |
    Where-Object { -not (Test-Path $_.Path) } |
    ForEach-Object { $_.Name }

if ($missingWorkerPayload.Count -gt 0) {
    Write-Host "Missing required processing worker payload: $($missingWorkerPayload -join ', ')"
    exit 1
}

$bundleMode = Get-BundleMode -Path $bundleModePath
$requiresDotNetRuntime = $bundleMode -eq "framework-dependent"

if ($requiresDotNetRuntime -and -not (Test-WindowsDesktopRuntime)) {
    Write-Host "This Meeting Recorder bundle requires the .NET 8 Desktop Runtime, but it was not detected."
    Write-Host "Run Install-Dependencies.cmd in this folder, or install it manually from:"
    Write-Host "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
    if (Test-Path $setupPath) {
        Write-Host "Additional guidance is available in: $setupPath"
    }

    exit 1
}

if (-not (Test-VisualCppRuntime)) {
    if (-not $SuppressOptionalWarnings) {
        Write-Host "Note: Microsoft Visual C++ x64 runtime was not detected."
        Write-Host "If the app launches but the transcription worker fails immediately, run Install-Dependencies.cmd."
    }
}

$bundledModelCount = if (Test-Path $modelDirectory) {
    (Get-ChildItem -Path $modelDirectory -Filter "*.bin" -File | Measure-Object).Count
}
else {
    0
}

if ($bundledModelCount -eq 0) {
    if (-not $SuppressOptionalWarnings) {
        Write-Host "Note: no Whisper model files were found under $modelDirectory."
        Write-Host "The app can still launch, but transcription will require downloading or importing a model from the Models tab."
    }
}

if (-not $QuietSuccess) {
    Write-Host "Dependency check passed. Bundle mode: $bundleMode."
}
exit 0
