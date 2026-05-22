param(
    [string]$SherpaRepository = "https://github.com/k2-fsa/sherpa-onnx.git",
    [string]$SherpaRef = "v1.13.0",
    [string]$OnnxRuntimeDirectMlVersion = "1.17.1",
    [string]$OutputRoot = "assets\native\sherpa-onnx-directml\win-x64",
    [string]$WorkRoot = ".tmp\sherpa-directml-runtime",
    [string]$Configuration = "Release",
    [switch]$UsePreinstalledOnnxRuntime,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputRoot = Join-Path $repoRoot $OutputRoot
$resolvedWorkRoot = Join-Path $repoRoot $WorkRoot
$sourceRoot = Join-Path $resolvedWorkRoot "sherpa-onnx"
$buildRoot = Join-Path $resolvedWorkRoot "build"
$installRoot = Join-Path $resolvedWorkRoot "install"
$dependencyRoot = Join-Path $resolvedWorkRoot "deps"
$onnxRuntimeDependencyMode = "sherpa-managed"

function Invoke-CheckedCommand {
    param(
        [string]$FileName,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    Write-Host ("Running: {0} {1}" -f $FileName, ($Arguments -join " "))
    Push-Location $WorkingDirectory
    try {
        & $FileName @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$FileName failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Assert-CommandAvailable {
    param(
        [string]$CommandName,
        [string]$InstallHint
    )

    if ($null -eq (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$CommandName' was not found. $InstallHint"
    }
}

function Expand-NuGetPackage {
    param(
        [string]$PackageId,
        [string]$Version,
        [string]$DestinationRoot
    )

    $packageDirectory = Join-Path $DestinationRoot "$PackageId.$Version"
    if (Test-Path $packageDirectory) {
        return $packageDirectory
    }

    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null
    $packagePath = Join-Path $DestinationRoot "$PackageId.$Version.nupkg"
    $zipPath = Join-Path $DestinationRoot "$PackageId.$Version.zip"
    $packageUrl = "https://globalcdn.nuget.org/packages/$($PackageId.ToLowerInvariant()).$Version.nupkg"
    Invoke-WebRequest -Uri $packageUrl -OutFile $packagePath
    Copy-Item -Path $packagePath -Destination $zipPath -Force
    Expand-Archive -Path $zipPath -DestinationPath $packageDirectory -Force
    return $packageDirectory
}

function Get-FileSha256 {
    param(
        [string]$Path
    )

    return (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

Assert-CommandAvailable -CommandName "git" -InstallHint "Install Git before building the bundled Sherpa DirectML runtime."
Assert-CommandAvailable -CommandName "cmake" -InstallHint "Install CMake and Visual Studio 2022 C++ build tools before building the bundled Sherpa DirectML runtime."

if ($Clean.IsPresent) {
    Remove-Item -Recurse -Force $resolvedWorkRoot -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $resolvedWorkRoot | Out-Null
New-Item -ItemType Directory -Force -Path $dependencyRoot | Out-Null

if (-not (Test-Path $sourceRoot)) {
    Invoke-CheckedCommand `
        -FileName "git" `
        -Arguments @("clone", "--depth", "1", "--branch", $SherpaRef, $SherpaRepository, $sourceRoot) `
        -WorkingDirectory $resolvedWorkRoot
}
else {
    Invoke-CheckedCommand -FileName "git" -Arguments @("-C", $sourceRoot, "fetch", "--depth", "1", "origin", $SherpaRef) -WorkingDirectory $resolvedWorkRoot
    Invoke-CheckedCommand -FileName "git" -Arguments @("-C", $sourceRoot, "checkout", "FETCH_HEAD") -WorkingDirectory $resolvedWorkRoot
}

if ($UsePreinstalledOnnxRuntime.IsPresent) {
    $onnxRuntimeRoot = Expand-NuGetPackage `
        -PackageId "Microsoft.ML.OnnxRuntime.DirectML" `
        -Version $OnnxRuntimeDirectMlVersion `
        -DestinationRoot $dependencyRoot

    $env:SHERPA_ONNXRUNTIME_INCLUDE_DIR = Join-Path $onnxRuntimeRoot "build\native\include"
    $env:SHERPA_ONNXRUNTIME_LIB_DIR = Join-Path $onnxRuntimeRoot "runtimes\win-x64\native"
    $onnxRuntimeDependencyMode = "preinstalled"

    if (-not (Test-Path (Join-Path $env:SHERPA_ONNXRUNTIME_INCLUDE_DIR "onnxruntime_c_api.h"))) {
        throw "ONNX Runtime DirectML headers were not found under '$env:SHERPA_ONNXRUNTIME_INCLUDE_DIR'."
    }

    if (-not (Test-Path (Join-Path $env:SHERPA_ONNXRUNTIME_LIB_DIR "onnxruntime.lib"))) {
        throw "ONNX Runtime DirectML import library was not found under '$env:SHERPA_ONNXRUNTIME_LIB_DIR'."
    }
}
else {
    Remove-Item Env:SHERPA_ONNXRUNTIME_INCLUDE_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:SHERPA_ONNXRUNTIME_LIB_DIR -ErrorAction SilentlyContinue
}

Remove-Item -Recurse -Force $buildRoot -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $installRoot -ErrorAction SilentlyContinue

$configureArguments = @(
    "-S", $sourceRoot,
    "-B", $buildRoot,
    "-G", "Visual Studio 17 2022",
    "-A", "x64",
    "-DCMAKE_INSTALL_PREFIX=$installRoot",
    "-DBUILD_SHARED_LIBS=ON",
    "-DSHERPA_ONNX_ENABLE_DIRECTML=ON",
    "-DSHERPA_ONNX_ENABLE_BINARY=OFF",
    "-DSHERPA_ONNX_ENABLE_TESTS=OFF",
    "-DSHERPA_ONNX_ENABLE_PYTHON=OFF",
    "-DSHERPA_ONNX_ENABLE_PORTAUDIO=OFF",
    "-DSHERPA_ONNX_ENABLE_JNI=OFF",
    "-DSHERPA_ONNX_ENABLE_TTS=OFF"
)

Invoke-CheckedCommand -FileName "cmake" -Arguments $configureArguments -WorkingDirectory $resolvedWorkRoot
Invoke-CheckedCommand `
    -FileName "cmake" `
    -Arguments @("--build", $buildRoot, "--config", $Configuration, "--target", "install", "--parallel", [Environment]::ProcessorCount.ToString([Globalization.CultureInfo]::InvariantCulture)) `
    -WorkingDirectory $resolvedWorkRoot

$runtimeSearchRoots = @(
    (Join-Path $installRoot "bin"),
    (Join-Path $buildRoot "bin\$Configuration")
) | Where-Object { Test-Path $_ }

$requiredRuntimeFiles = @(
    "sherpa-onnx-c-api.dll",
    "onnxruntime.dll",
    "DirectML.dll"
)

$optionalRuntimeFiles = @(
    "sherpa-onnx-cxx-api.dll",
    "onnxruntime_providers_shared.dll",
    "onnxruntime_providers_dml.dll"
)

Remove-Item -Recurse -Force $resolvedOutputRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $resolvedOutputRoot | Out-Null

foreach ($runtimeFile in ($requiredRuntimeFiles + $optionalRuntimeFiles)) {
    $sourceFile = $runtimeSearchRoots |
        ForEach-Object { Join-Path $_ $runtimeFile } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if ($null -eq $sourceFile) {
        if ($requiredRuntimeFiles -contains $runtimeFile) {
            throw "DirectML runtime build did not produce required file '$runtimeFile'."
        }

        continue
    }

    Copy-Item -Path $sourceFile -Destination (Join-Path $resolvedOutputRoot $runtimeFile) -Force
}

$sherpaNativePath = Join-Path $resolvedOutputRoot "sherpa-onnx-c-api.dll"
$sherpaNativeText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($sherpaNativePath))
if ($sherpaNativeText.IndexOf("Failed to enable DirectML", [StringComparison]::Ordinal) -lt 0) {
    throw "Built sherpa-onnx-c-api.dll does not contain the expected DirectML enable path."
}

if ($sherpaNativeText.IndexOf("DirectML is for Windows only", [StringComparison]::Ordinal) -ge 0) {
    throw "Built sherpa-onnx-c-api.dll still contains the CPU-only DirectML fallback marker."
}

$runtimeFiles = Get-ChildItem -Path $resolvedOutputRoot -File -Filter "*.dll" | Sort-Object Name
$manifest = [ordered]@{
    formatVersion              = 1
    builtAtUtc                 = [DateTimeOffset]::UtcNow.ToString("O")
    sherpaRepository           = $SherpaRepository
    sherpaRef                  = $SherpaRef
    onnxRuntimeDependencyMode  = $onnxRuntimeDependencyMode
    onnxRuntimeDirectMlVersion = $OnnxRuntimeDirectMlVersion
    runtime                    = "win-x64"
    files                      = @(
        $runtimeFiles | ForEach-Object {
            [ordered]@{
                name        = $_.Name
                lengthBytes = [int64]$_.Length
                sha256      = Get-FileSha256 -Path $_.FullName
            }
        }
    )
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $resolvedOutputRoot "sherpa-directml-runtime.json") -Encoding UTF8

Write-Host "DirectML-enabled Sherpa runtime staged at '$resolvedOutputRoot'."
