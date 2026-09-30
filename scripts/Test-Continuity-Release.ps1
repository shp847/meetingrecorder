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
    & dotnet test (Join-Path $repoRoot "tests\MeetingRecorder.Core.Tests\MeetingRecorder.Core.Tests.csproj") `
        -c Release -p:NuGetAudit=false --filter "FullyQualifiedName~Continuity|FullyQualifiedName~OngoingMeetingHeal|FullyQualifiedName~MeetingIdentity|FullyQualifiedName~AutoRecordingContinuityPolicyTests|FullyQualifiedName~MainWindowStartupSourceTests|FullyQualifiedName~RecordingStopPipelineSourceTests|FullyQualifiedName~AppConfigStoreTests"
    if ($LASTEXITCODE -ne 0) { throw "Continuity release test journey failed." }

    $releaseTestedCorePath = Join-Path $repoRoot "src\MeetingRecorder.Core\bin\Release\net8.0-windows\MeetingRecorder.Core.dll"
    Assert-SameHash -ExpectedPath $releaseTestedCorePath -ActualPath $bundleCorePath -Label "Release-tested core and portable bundle core"
}

if (-not $SkipInstalledHash.IsPresent) {
    $installedCorePath = Join-Path $resolvedInstallRoot "MeetingRecorder.Core.dll"
    Assert-SameHash -ExpectedPath $bundleCorePath -ActualPath $installedCorePath -Label "Portable bundle and installed core"
}

Write-Host "Continuity release validation passed. Package has no trace payload; release-tested core matches the portable bundle$($(if ($SkipInstalledHash.IsPresent) { '' } else { ' and installed bundle' }))."
