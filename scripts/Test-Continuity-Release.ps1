param(
    [string]$Runtime = "win-x64",
    [string]$BundleRoot = "",
    [string]$InstallRoot = "",
    [switch]$SkipReleaseTests,
    [switch]$SkipInstalledHash
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedBundleRoot = if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    Join-Path $repoRoot ".artifacts\publish\$Runtime\MeetingRecorder"
}
elseif ([System.IO.Path]::IsPathRooted($BundleRoot)) { $BundleRoot }
else { Join-Path $repoRoot $BundleRoot }
$resolvedBundleRoot = [System.IO.Path]::GetFullPath($resolvedBundleRoot)

function Assert-NoRunningMeetingRecorderInstances {
    $running = Get-Process -Name "MeetingRecorder.App" -ErrorAction SilentlyContinue
    if ($null -ne $running -and $running.Count -gt 0) {
        throw "Close Meeting Recorder before validating continuity release evidence."
    }
}

function Get-Hash {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Required file is missing: '$Path'." }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Assert-SameHash {
    param([string]$ExpectedPath, [string]$ActualPath, [string]$Label)
    $expected = Get-Hash $ExpectedPath
    $actual = Get-Hash $ActualPath
    if ($expected -ne $actual) {
        throw "$Label differs. Expected SHA-256 $expected from '$ExpectedPath'; got $actual from '$ActualPath'."
    }
}

Assert-NoRunningMeetingRecorderInstances
$manifestPath = Join-Path $resolvedBundleRoot "MeetingRecorder.product.json"
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Bundle manifest is missing: '$manifestPath'." }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$resolvedInstallRoot = if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    [Environment]::ExpandEnvironmentVariables([string]$manifest.managedInstallLayout.installRoot)
}
elseif ([System.IO.Path]::IsPathRooted($InstallRoot)) { $InstallRoot }
else { Join-Path $repoRoot $InstallRoot }
$resolvedInstallRoot = [System.IO.Path]::GetFullPath($resolvedInstallRoot)

$bundleCorePath = Join-Path $resolvedBundleRoot "MeetingRecorder.Core.dll"
foreach ($required in @(
    $bundleCorePath,
    (Join-Path $resolvedBundleRoot "MeetingRecorder.App.exe"),
    (Join-Path $resolvedBundleRoot "MeetingRecorder.ProcessingWorker.exe"),
    (Join-Path $resolvedBundleRoot "AppPlatform.Deployment.Cli.exe"),
    (Join-Path $resolvedBundleRoot "bundle-integrity.json")))
{
    if (-not (Test-Path -LiteralPath $required)) { throw "Required continuity release payload is missing: '$required'." }
}

$tracePayload = Get-ChildItem -LiteralPath $resolvedBundleRoot -Recurse -Force |
    Where-Object { $_.Name -match '(?i)continuity.*trace|trace.*continuity' }
if ($tracePayload) {
    throw "Release bundle contains diagnostic trace payload: $($tracePayload.FullName -join '; ')."
}

if (-not $SkipReleaseTests.IsPresent) {
    $testProjectPath = Join-Path $repoRoot "tests\MeetingRecorder.Core.Tests\MeetingRecorder.Core.Tests.csproj"
    & dotnet build $testProjectPath -c Release -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "Could not build the continuity release test harness." }

    $testOutputRoot = Join-Path $repoRoot "tests\MeetingRecorder.Core.Tests\bin\Release\net8.0-windows"
    $testAssemblyName = "MeetingRecorder.Core.Tests.dll"
    $testOutputPath = Join-Path $testOutputRoot $testAssemblyName
    if (-not (Test-Path -LiteralPath $testOutputPath)) { throw "Continuity release test harness is missing '$testOutputPath'." }
    $isolatedTestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("MeetingRecorderContinuityRelease-" + [guid]::NewGuid().ToString("N"))
    Copy-Item -LiteralPath $testOutputRoot -Destination $isolatedTestRoot -Recurse
    $isolatedTestAssembly = Join-Path $isolatedTestRoot $testAssemblyName
    Copy-Item -LiteralPath $bundleCorePath -Destination (Join-Path $isolatedTestRoot "MeetingRecorder.Core.dll") -Force
    $testFilter = "FullyQualifiedName~Continuity|FullyQualifiedName~OngoingMeetingHeal|FullyQualifiedName~MeetingIdentity|FullyQualifiedName~AutoRecordingContinuityPolicyTests|FullyQualifiedName~MainWindowStartupSourceTests|FullyQualifiedName~RecordingStopPipelineSourceTests|FullyQualifiedName~AppConfigStoreTests"
    try {
        & dotnet vstest $isolatedTestAssembly ("--TestCaseFilter:" + $testFilter)
        if ($LASTEXITCODE -ne 0) { throw "Bundled-Core continuity release test journey failed." }
    }
    finally {
        if (Test-Path -LiteralPath $isolatedTestRoot) { Remove-Item -LiteralPath $isolatedTestRoot -Recurse -Force }
    }
}

if (-not $SkipInstalledHash.IsPresent) {
    $installedCorePath = Join-Path $resolvedInstallRoot "MeetingRecorder.Core.dll"
    Assert-SameHash -ExpectedPath $bundleCorePath -ActualPath $installedCorePath -Label "Portable bundle and installed core"
}

Write-Host "Continuity release validation passed. Package has no trace payload; the exact bundled Core DLL passed the release continuity journey$($(if ($SkipInstalledHash.IsPresent) { '' } else { ' and matches the installed bundle' }))."
