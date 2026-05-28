param(
    [Parameter(Mandatory = $true)]
    [string]$JsonPath,

    [string]$ManifestPath,

    [int]$ExpectedSpeakerCount = 0,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$NoBuild,

    [string]$ResultPath
)

$ErrorActionPreference = "Stop"

function Resolve-RequiredFile {
    param(
        [string]$PathValue,
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "$Description path is required."
    }

    $resolved = Resolve-Path -LiteralPath $PathValue -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.ProviderPath -PathType Leaf)) {
        throw "$Description path is not a file: $($resolved.ProviderPath)"
    }

    return $resolved.ProviderPath
}

if ($ExpectedSpeakerCount -ne 0 -and ($ExpectedSpeakerCount -lt 2 -or $ExpectedSpeakerCount -gt 16)) {
    throw "ExpectedSpeakerCount must be 2-16 when supplied."
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).ProviderPath
$projectPath = Join-Path $repoRoot "tests\MeetingRecorder.Core.Tests\MeetingRecorder.Core.Tests.csproj"
$resolvedJsonPath = Resolve-RequiredFile -PathValue $JsonPath -Description "Transcript JSON"
$resolvedManifestPath = $null
if (-not [string]::IsNullOrWhiteSpace($ManifestPath)) {
    $resolvedManifestPath = Resolve-RequiredFile -PathValue $ManifestPath -Description "Manifest"
}

if ([string]::IsNullOrWhiteSpace($ResultPath)) {
    $ResultPath = Join-Path ([System.IO.Path]::GetTempPath()) ("meeting-recorder-diarization-fixture-{0}.json" -f [Guid]::NewGuid().ToString("N"))
}

$resolvedResultPath = [System.IO.Path]::GetFullPath($ResultPath)
$previousJson = $env:MEETING_RECORDER_DIARIZATION_FIXTURE_JSON
$previousManifest = $env:MEETING_RECORDER_DIARIZATION_FIXTURE_MANIFEST
$previousExpected = $env:MEETING_RECORDER_DIARIZATION_FIXTURE_EXPECTED_SPEAKERS
$previousResult = $env:MEETING_RECORDER_DIARIZATION_FIXTURE_RESULT_PATH

try {
    $env:MEETING_RECORDER_DIARIZATION_FIXTURE_JSON = $resolvedJsonPath
    $env:MEETING_RECORDER_DIARIZATION_FIXTURE_MANIFEST = $resolvedManifestPath
    $env:MEETING_RECORDER_DIARIZATION_FIXTURE_EXPECTED_SPEAKERS = if ($ExpectedSpeakerCount -eq 0) { "" } else { [string]$ExpectedSpeakerCount }
    $env:MEETING_RECORDER_DIARIZATION_FIXTURE_RESULT_PATH = $resolvedResultPath

    $filter = "FullyQualifiedName~DiarizationFixtureReplayTests.ExternalFixture_Replays_SpeakerClusterMerge_When_Configured"
    $dotnetArgs = @(
        "test",
        $projectPath,
        "-c",
        $Configuration,
        "--filter",
        $filter,
        "-p:NuGetAudit=false",
        "--logger",
        "console;verbosity=minimal")
    if ($NoBuild) {
        $dotnetArgs += "--no-build"
        $dotnetArgs += "--no-restore"
    }

    $displayCommand = "dotnet test `"$projectPath`" -c $Configuration --filter `"$filter`" -p:NuGetAudit=false"
    if ($NoBuild) {
        $displayCommand += " --no-build --no-restore"
    }

    Write-Output "Running diarization fixture replay:"
    Write-Output "  $displayCommand"
    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Diarization fixture replay failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $resolvedResultPath -PathType Leaf)) {
        throw "Fixture replay did not write a result file: $resolvedResultPath"
    }

    $result = Get-Content -LiteralPath $resolvedResultPath -Raw | ConvertFrom-Json
    Write-Output ""
    Write-Output "Diarization fixture replay result:"
    Write-Output "  Transcript JSON: $($result.TranscriptJsonPath)"
    if ($result.ManifestPath) {
        Write-Output "  Manifest: $($result.ManifestPath)"
    }
    Write-Output "  Original speakers: $($result.OriginalSpeakerCount)"
    Write-Output "  Merged speakers: $($result.MergedSpeakerCount)"
    Write-Output "  Original speaker turns: $($result.OriginalSpeakerTurnCount)"
    Write-Output "  Merged speaker turns: $($result.MergedSpeakerTurnCount)"
    Write-Output "  Voice samples: $($result.VoiceSampleCount)"
    if ($result.ExpectedSpeakerCount) {
        Write-Output "  Expected speakers: $($result.ExpectedSpeakerCount)"
    }
    Write-Output "  Status: $($result.Status)"
    if ($result.DiagnosticMessage) {
        Write-Output "  Diagnostic: $($result.DiagnosticMessage)"
    }
    Write-Output "  Result JSON: $resolvedResultPath"
}
finally {
    $env:MEETING_RECORDER_DIARIZATION_FIXTURE_JSON = $previousJson
    $env:MEETING_RECORDER_DIARIZATION_FIXTURE_MANIFEST = $previousManifest
    $env:MEETING_RECORDER_DIARIZATION_FIXTURE_EXPECTED_SPEAKERS = $previousExpected
    $env:MEETING_RECORDER_DIARIZATION_FIXTURE_RESULT_PATH = $previousResult
}
