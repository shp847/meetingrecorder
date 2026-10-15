param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [string]$ConfigPath,

    [string]$AudioPath,

    [string]$TranscriptionSnapshotPath,

    [ValidateSet("Configured", "Standard", "HighAccuracy")]
    [string]$SpeakerLabelingProfile = "Configured",

    [string]$DiarizationAssetPath,

    [ValidateSet("CpuOnly", "Auto")]
    [string]$AccelerationPreference = "CpuOnly",

    [int]$ExpectedSpeakerCount = 0,

    [string[]]$ExpectedSpeakerName = @(),

    [switch]$EnableSpeakerNameMatching,

    [string[]]$PublishedArtifactPath = @(),

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$ProbeRoot,

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

function Resolve-OptionalFile {
    param(
        [string]$PathValue,
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    return Resolve-RequiredFile -PathValue $PathValue -Description $Description
}

function Resolve-ExistingDirectory {
    param(
        [string]$PathValue,
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "$Description path is required."
    }

    $resolved = Resolve-Path -LiteralPath $PathValue -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.ProviderPath -PathType Container)) {
        throw "$Description path is not a directory: $($resolved.ProviderPath)"
    }

    return $resolved.ProviderPath
}

function Get-ResolvedArtifactHashes {
    param([string[]]$Paths)

    $hashes = @()
    foreach ($pathValue in $Paths) {
        foreach ($expandedPathValue in @($pathValue -split ",")) {
            if ([string]::IsNullOrWhiteSpace($expandedPathValue)) {
                continue
            }

            $resolvedPath = Resolve-RequiredFile -PathValue $expandedPathValue.Trim() -Description "Published artifact"
            $hashes += [pscustomobject]@{
                Path = $resolvedPath
                Hash = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash
            }
        }
    }

    return $hashes
}

function Get-ManifestPropertyValue {
    param(
        $Object,
        [string]$PropertyName
    )

    if ($null -eq $Object -or -not ($Object.PSObject.Properties.Name -contains $PropertyName)) {
        return $null
    }

    return $Object.$PropertyName
}

function Set-ObjectPropertyValue {
    param(
        $Object,
        [string]$PropertyName,
        $Value
    )

    if ($Object.PSObject.Properties.Name -contains $PropertyName) {
        $Object.$PropertyName = $Value
        return
    }

    $Object | Add-Member -NotePropertyName $PropertyName -NotePropertyValue $Value
}

function Resolve-SourceAudioPath {
    param(
        $Manifest,
        [string]$SessionRoot,
        [string]$ExplicitAudioPath
    )

    $explicit = Resolve-OptionalFile -PathValue $ExplicitAudioPath -Description "Audio"
    if ($explicit) {
        return $explicit
    }

    $mergedAudioPath = Get-ManifestPropertyValue -Object $Manifest -PropertyName "mergedAudioPath"
    if (-not [string]::IsNullOrWhiteSpace($mergedAudioPath) -and (Test-Path -LiteralPath $mergedAudioPath -PathType Leaf)) {
        return (Resolve-Path -LiteralPath $mergedAudioPath).ProviderPath
    }

    $importedSourceAudio = Get-ManifestPropertyValue -Object $Manifest -PropertyName "importedSourceAudio"
    $importedOriginalPath = Get-ManifestPropertyValue -Object $importedSourceAudio -PropertyName "originalPath"
    if (-not [string]::IsNullOrWhiteSpace($importedOriginalPath) -and (Test-Path -LiteralPath $importedOriginalPath -PathType Leaf)) {
        return (Resolve-Path -LiteralPath $importedOriginalPath).ProviderPath
    }

    $importedSourcePath = Join-Path $SessionRoot "processing\imported-source.wav"
    if (Test-Path -LiteralPath $importedSourcePath -PathType Leaf) {
        return (Resolve-Path -LiteralPath $importedSourcePath).ProviderPath
    }

    $processingRoot = Join-Path $SessionRoot "processing"
    $candidate = Get-ChildItem -LiteralPath $processingRoot -Filter "*.wav" -File -ErrorAction SilentlyContinue |
        Sort-Object Length -Descending |
        Select-Object -First 1
    if ($candidate) {
        return $candidate.FullName
    }

    throw "Unable to find full-audio WAV for manifest '$ManifestPath'. Pass -AudioPath explicitly."
}

function Resolve-SnapshotPath {
    param(
        [string]$SessionRoot,
        [string]$ExplicitSnapshotPath
    )

    $explicit = Resolve-OptionalFile -PathValue $ExplicitSnapshotPath -Description "Transcription snapshot"
    if ($explicit) {
        return $explicit
    }

    return Resolve-RequiredFile `
        -PathValue (Join-Path $SessionRoot "processing\transcription.snapshot.json") `
        -Description "Transcription snapshot"
}

function Set-SpeakerLabelingAsset {
    param(
        $Config,
        [string]$RequestedProfile,
        [string]$ExplicitAssetPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitAssetPath)) {
        $Config.diarizationAssetPath = Resolve-ExistingDirectory -PathValue $ExplicitAssetPath -Description "Diarization asset"
        $Config.speakerLabelingModelProfilePreference = 2
        return
    }

    if ($RequestedProfile -eq "Configured") {
        return
    }

    if ([string]::IsNullOrWhiteSpace($Config.modelCacheDir)) {
        throw "Config does not contain modelCacheDir; pass -DiarizationAssetPath explicitly."
    }

    if ($RequestedProfile -eq "Standard") {
        $Config.diarizationAssetPath = Resolve-ExistingDirectory `
            -PathValue (Join-Path $Config.modelCacheDir "diarization") `
            -Description "Standard diarization asset"
        $Config.speakerLabelingModelProfilePreference = 0
        return
    }

    $Config.diarizationAssetPath = Resolve-ExistingDirectory `
        -PathValue (Join-Path $Config.modelCacheDir "diarization\high-accuracy") `
        -Description "High-accuracy diarization asset"
    $Config.speakerLabelingModelProfilePreference = 1
}

if ($ExpectedSpeakerCount -ne 0 -and ($ExpectedSpeakerCount -lt 2 -or $ExpectedSpeakerCount -gt 16)) {
    throw "ExpectedSpeakerCount must be 2-16 when supplied."
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).ProviderPath
$resolvedManifestPath = Resolve-RequiredFile -PathValue $ManifestPath -Description "Manifest"
$sessionRoot = Split-Path -Path $resolvedManifestPath -Parent
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
$sourceAudioPath = Resolve-SourceAudioPath -Manifest $manifest -SessionRoot $sessionRoot -ExplicitAudioPath $AudioPath
$sourceSnapshotPath = Resolve-SnapshotPath -SessionRoot $sessionRoot -ExplicitSnapshotPath $TranscriptionSnapshotPath
$resolvedConfigPath = if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    Resolve-RequiredFile `
        -PathValue (Join-Path $env:LOCALAPPDATA "MeetingRecorder\config\appsettings.json") `
        -Description "App config"
}
else {
    Resolve-RequiredFile -PathValue $ConfigPath -Description "App config"
}

$frameworkWorkerDirectory = Join-Path $repoRoot "src\MeetingRecorder.ProcessingWorker\bin\$Configuration\net8.0-windows"
$runtimeWorkerDirectory = Join-Path $frameworkWorkerDirectory "win-x64"
$frameworkWorkerDllPath = Join-Path $frameworkWorkerDirectory "MeetingRecorder.ProcessingWorker.dll"
$runtimeWorkerDllPath = Join-Path $runtimeWorkerDirectory "MeetingRecorder.ProcessingWorker.dll"
$runtimeWorkerIsCurrent =
    (Test-Path -LiteralPath $runtimeWorkerDllPath -PathType Leaf) -and
    (Test-Path -LiteralPath $frameworkWorkerDllPath -PathType Leaf) -and
    ((Get-Item -LiteralPath $runtimeWorkerDllPath).LastWriteTimeUtc -ge (Get-Item -LiteralPath $frameworkWorkerDllPath).LastWriteTimeUtc)

if (-not $runtimeWorkerIsCurrent) {
    # Full-audio fixtures need the RID-specific worker because it carries the
    # native Sherpa dependencies, including the CPU payload. A normal framework build cannot satisfy that
    # contract, so refresh the RID output rather than falling back to it.
    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $workerProjectPath = Join-Path $repoRoot "src\MeetingRecorder.ProcessingWorker\MeetingRecorder.ProcessingWorker.csproj"
    & $dotnetCommand.Source build $workerProjectPath -t:Rebuild -c $Configuration -r win-x64 -p:UseAppHost=false -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to build the win-x64 processing worker required for full-audio fixture execution."
    }

    # A successful forced rebuild is the freshness authority. File timestamps
    # are only the trigger: copy operations may preserve an older timestamp.
    $runtimeWorkerIsCurrent = Test-Path -LiteralPath $runtimeWorkerDllPath -PathType Leaf
}

if (-not $runtimeWorkerIsCurrent) {
    throw "Current win-x64 processing worker output is required for full-audio fixture execution."
}

foreach ($nativePayloadFile in @("sherpa-onnx-c-api.dll", "onnxruntime.dll")) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtimeWorkerDirectory $nativePayloadFile) -PathType Leaf)) {
        throw "Current win-x64 processing worker is missing required CPU diarization native payload."
    }
}

$workerDirectory = $runtimeWorkerDirectory
$workerAppHostPath = Join-Path $workerDirectory "MeetingRecorder.ProcessingWorker.exe"
$workerDllPath = Join-Path $workerDirectory "MeetingRecorder.ProcessingWorker.dll"
$workerArguments = @()
if (Test-Path -LiteralPath $workerAppHostPath -PathType Leaf) {
    $workerCommandPath = Resolve-RequiredFile -PathValue $workerAppHostPath -Description "Processing worker apphost"
}
elseif (Test-Path -LiteralPath $workerDllPath -PathType Leaf) {
    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $workerCommandPath = $dotnetCommand.Source
    $workerArguments += (Resolve-RequiredFile -PathValue $workerDllPath -Description "Processing worker assembly")
}
else {
    throw "Processing worker apphost and assembly are both missing under '$workerDirectory'."
}
$protectedArtifactHashesBefore = Get-ResolvedArtifactHashes -Paths $PublishedArtifactPath

if ([string]::IsNullOrWhiteSpace($ProbeRoot)) {
    $ProbeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("meeting-recorder-full-diarization-probe-{0}" -f [Guid]::NewGuid().ToString("N"))
}

$resolvedProbeRoot = [System.IO.Path]::GetFullPath($ProbeRoot)
$probeSessionRoot = Join-Path $resolvedProbeRoot "session"
$probeProcessingRoot = Join-Path $probeSessionRoot "processing"
$probeLogsRoot = Join-Path $probeSessionRoot "logs"
$probeOutputRoot = Join-Path $resolvedProbeRoot "probe-output"
$probeRecordingsRoot = Join-Path $probeOutputRoot "recordings"
$probeTranscriptsRoot = Join-Path $probeOutputRoot "transcripts"
$probeConfigRoot = Join-Path $resolvedProbeRoot "config"

New-Item -ItemType Directory `
    -Path $probeProcessingRoot, $probeLogsRoot, $probeRecordingsRoot, $probeTranscriptsRoot, $probeConfigRoot `
    -Force | Out-Null
Copy-Item -LiteralPath $sourceSnapshotPath -Destination (Join-Path $probeProcessingRoot "transcription.snapshot.json") -Force

if ($null -eq $manifest.processingOverrides) {
    $manifest | Add-Member -NotePropertyName processingOverrides -NotePropertyValue ([pscustomobject]@{})
}

Set-ObjectPropertyValue -Object $manifest.processingOverrides -PropertyName "skipSpeakerLabeling" -Value $false
Set-ObjectPropertyValue -Object $manifest -PropertyName "mergedAudioPath" -Value $sourceAudioPath

$probeManifestPath = Join-Path $probeSessionRoot "manifest.json"
$manifest | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $probeManifestPath -Encoding UTF8

$config = Get-Content -LiteralPath $resolvedConfigPath -Raw | ConvertFrom-Json
$config.audioOutputDir = $probeRecordingsRoot
$config.transcriptOutputDir = $probeTranscriptsRoot
$config.workDir = Join-Path $resolvedProbeRoot "work"
$config.summaryGenerationMode = 0
$config.speakerNameLearningMode = if ($EnableSpeakerNameMatching) { 1 } else { 0 }
$config.diarizationAccelerationPreference = if ($AccelerationPreference -eq "Auto") { 0 } else { 1 }
Set-SpeakerLabelingAsset -Config $config -RequestedProfile $SpeakerLabelingProfile -ExplicitAssetPath $DiarizationAssetPath

$probeConfigPath = Join-Path $probeConfigRoot "appsettings.json"
$config | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath $probeConfigPath -Encoding UTF8

Write-Output "Running full-audio diarization fixture:"
Write-Output "  Worker: $workerCommandPath"
Write-Output "  Source manifest: $resolvedManifestPath"
Write-Output "  Source audio: $sourceAudioPath"
Write-Output "  Transcription snapshot: $sourceSnapshotPath"
Write-Output "  Probe root: $resolvedProbeRoot"
Write-Output "  Output transcripts: $probeTranscriptsRoot"
Write-Output "  Speaker-labeling profile: $SpeakerLabelingProfile"
Write-Output "  Diarization assets: $($config.diarizationAssetPath)"
Write-Output "  Acceleration: $AccelerationPreference"
Write-Output "  Speaker-name matching: $([bool]$EnableSpeakerNameMatching)"

$started = Get-Date
& $workerCommandPath @workerArguments --manifest $probeManifestPath --config $probeConfigPath
$exitCode = $LASTEXITCODE
$elapsed = (Get-Date) - $started
Write-Output "Worker exit code: $exitCode"
Write-Output ("Elapsed: {0:c}" -f $elapsed)

$updatedManifest = Get-Content -LiteralPath $probeManifestPath -Raw | ConvertFrom-Json
$diarizationMessage = $updatedManifest.diarizationStatus.message
if ([string]::IsNullOrWhiteSpace($diarizationMessage)) {
    # Some terminal worker failures are recorded only as the session-level
    # summary. Keep this in the ephemeral child result so the catalog can map
    # it to a fixed, privacy-safe failure class before it writes its report.
    $diarizationMessage = $updatedManifest.errorSummary
}
$speakers = @($updatedManifest.processingMetadata.speakers)
$turns = @($updatedManifest.processingMetadata.speakerTurns)
$samples = @($updatedManifest.processingMetadata.speakerVoiceSamples)
$speakerNames = @($speakers | ForEach-Object {
        $names = @()
        if (-not [string]::IsNullOrWhiteSpace($_.displayName)) {
            $names += $_.displayName
        }
        if (-not [string]::IsNullOrWhiteSpace($_.suggestedDisplayName)) {
            $names += $_.suggestedDisplayName
        }
        $names
    } | Select-Object -Unique)
$safeSpeakerIds = @{}
$safeSpeakerTurnIndex = 0
$safeSpeakerTurns = @($turns | ForEach-Object {
        $sourceSpeakerId = [string]$_.speakerId
        if ([string]::IsNullOrWhiteSpace($sourceSpeakerId)) {
            return
        }
        if (-not $safeSpeakerIds.ContainsKey($sourceSpeakerId)) {
            $safeSpeakerTurnIndex++
            $safeSpeakerIds[$sourceSpeakerId] = ('detected-{0:D2}' -f $safeSpeakerTurnIndex)
        }
        $start = [TimeSpan]$_.start
        $end = [TimeSpan]$_.end
        if ($end -le $start) {
            return
        }
        [ordered]@{
            speakerId = $safeSpeakerIds[$sourceSpeakerId]
            startMilliseconds = [long]$start.TotalMilliseconds
            endMilliseconds = [long]$end.TotalMilliseconds
        }
    })
$missingExpectedSpeakerNames = @($ExpectedSpeakerName | Where-Object {
        $expectedName = $_
        -not ($speakerNames | Where-Object { $_ -eq $expectedName })
    })
$protectedArtifacts = @()
foreach ($before in $protectedArtifactHashesBefore) {
    $afterHash = (Get-FileHash -LiteralPath $before.Path -Algorithm SHA256).Hash
    $protectedArtifacts += [pscustomobject]@{
        Path = $before.Path
        BeforeHash = $before.Hash
        AfterHash = $afterHash
        Unchanged = $before.Hash -eq $afterHash
    }
}

$result = [pscustomobject]@{
    ProbeRoot = $resolvedProbeRoot
    ExitCode = $exitCode
    Elapsed = ("{0:c}" -f $elapsed)
    SourceManifestPath = $resolvedManifestPath
    SourceAudioPath = $sourceAudioPath
    SourceTranscriptionSnapshotPath = $sourceSnapshotPath
    DiarizationAssetPath = $config.diarizationAssetPath
    SpeakerLabelingProfile = $SpeakerLabelingProfile
    AccelerationPreference = $AccelerationPreference
    HasSpeakerLabels = [bool]$updatedManifest.processingMetadata.hasSpeakerLabels
    SpeakerCount = $speakers.Count
    SpeakerTurnCount = $turns.Count
    SpeakerTurns = $safeSpeakerTurns
    VoiceSampleCount = $samples.Count
    SpeakerNames = $speakerNames
    ExpectedSpeakerCount = if ($ExpectedSpeakerCount -eq 0) { $null } else { $ExpectedSpeakerCount }
    ExpectedSpeakerNames = $ExpectedSpeakerName
    SpeakerNameStatus = if ($ExpectedSpeakerName.Count -eq 0) {
        "no expected speaker names supplied"
    }
    elseif ($missingExpectedSpeakerNames.Count -eq 0) {
        "matches expected speaker names"
    }
    else {
        "missing expected speaker name(s): $($missingExpectedSpeakerNames -join ', ')"
    }
    Status = if ($ExpectedSpeakerCount -eq 0) {
        "no expected speaker count supplied"
    }
    elseif ($speakers.Count -eq $ExpectedSpeakerCount) {
        "matches expected speaker count"
    }
    elseif ($speakers.Count -gt $ExpectedSpeakerCount) {
        "over-segmented"
    }
    else {
        "collapsed below expected speaker count"
    }
    DiarizationState = $updatedManifest.diarizationStatus.state
    DiarizationMessage = $diarizationMessage
    OutputTranscriptDir = $probeTranscriptsRoot
    ProtectedArtifacts = $protectedArtifacts
}

if ([string]::IsNullOrWhiteSpace($ResultPath)) {
    $ResultPath = Join-Path $resolvedProbeRoot "diarization-fixture-result.json"
}

$resolvedResultPath = [System.IO.Path]::GetFullPath($ResultPath)
New-Item -ItemType Directory -Path (Split-Path -Path $resolvedResultPath -Parent) -Force | Out-Null
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $resolvedResultPath -Encoding UTF8

Write-Output ""
Write-Output "Full-audio diarization fixture result:"
Write-Output "  Speaker count: $($result.SpeakerCount)"
Write-Output "  Speaker turns: $($result.SpeakerTurnCount)"
Write-Output "  Voice samples: $($result.VoiceSampleCount)"
if ($speakerNames.Count -gt 0) {
    Write-Output "  Speaker names: $($speakerNames -join ', ')"
}
if ($result.ExpectedSpeakerCount) {
    Write-Output "  Expected speakers: $($result.ExpectedSpeakerCount)"
}
if ($ExpectedSpeakerName.Count -gt 0) {
    Write-Output "  Expected speaker names: $($ExpectedSpeakerName -join ', ')"
    Write-Output "  Speaker-name status: $($result.SpeakerNameStatus)"
}
Write-Output "  Status: $($result.Status)"
Write-Output "  Diarization message: $($result.DiarizationMessage)"
Write-Output "  Result JSON: $resolvedResultPath"

$changedProtectedArtifacts = @($protectedArtifacts | Where-Object { -not $_.Unchanged })
if ($changedProtectedArtifacts.Count -gt 0) {
    $changedPaths = ($changedProtectedArtifacts | ForEach-Object { $_.Path }) -join "; "
    throw "Protected published artifact changed during fixture run: $changedPaths"
}

if ($exitCode -ne 0) {
    throw "Full-audio diarization fixture failed with exit code $exitCode."
}

if ($ExpectedSpeakerCount -ne 0 -and $speakers.Count -ne $ExpectedSpeakerCount) {
    throw "Expected $ExpectedSpeakerCount speaker(s), but detected $($speakers.Count)."
}

if ($missingExpectedSpeakerNames.Count -gt 0) {
    throw "Missing expected speaker name(s): $($missingExpectedSpeakerNames -join ', ')."
}
