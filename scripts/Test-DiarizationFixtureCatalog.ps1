param(
    [string]$CatalogPath,

    [string]$TruthStorePath,

    [string[]]$FixtureId = @(),

    [string[]]$Category = @(),

    [switch]$IncludeDisabled,

    [switch]$DryRun,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$NoBuild,

    [string]$ReportsRoot,

    [string]$ReportPath,

    [string]$CalibrationRunName,

    [string]$CalibrationParametersJson
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

    $resolvedPath = [System.IO.Path]::GetFullPath($PathValue)
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$Description path is not a file: $resolvedPath"
    }

    return $resolvedPath
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

function Resolve-RequiredDirectory {
    param(
        [string]$PathValue,
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "$Description path is required."
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($PathValue)
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Container)) {
        throw "$Description path is not a directory: $resolvedPath"
    }

    return $resolvedPath
}

function Resolve-CatalogPathValue {
    param(
        [string]$PathValue,
        [string]$Description,
        [switch]$Required
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        if ($Required) {
            throw "$Description path is required."
        }

        return $null
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($PathValue)
    if (-not [System.IO.Path]::IsPathRooted($expanded)) {
        $expanded = Join-Path $script:RepoRoot $expanded
    }

    return Resolve-RequiredFile -PathValue $expanded -Description $Description
}

function ConvertTo-Array {
    param($Value)

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [System.Array]) {
        return @($Value)
    }

    return @($Value)
}

function Get-PropertyValue {
    param(
        $Object,
        [string]$Name,
        $DefaultValue = $null
    )

    if ($null -eq $Object -or -not ($Object.PSObject.Properties.Name -contains $Name)) {
        return $DefaultValue
    }

    return $Object.$Name
}

function Test-PropertyPresent {
    param(
        $Object,
        [string]$Name
    )

    return $null -ne $Object -and ($Object.PSObject.Properties.Name -contains $Name)
}

function Get-TruthOutcome {
    param($Fixture)

    if ($script:CatalogSchemaVersion -lt 2) {
        return $null
    }

    $truthStore = Get-Content -LiteralPath $script:ResolvedTruthStorePath -Raw | ConvertFrom-Json
    if ([int](Get-PropertyValue -Object $truthStore -Name "schemaVersion" -DefaultValue 0) -ne 1) {
        throw "Restricted fixture truth store schemaVersion must be 1."
    }

    $outcomes = @{}
    foreach ($outcome in @(ConvertTo-Array (Get-PropertyValue -Object $truthStore -Name "outcomes"))) {
        $outcomeReference = [string](Get-PropertyValue -Object $outcome -Name "reference")
        if ([string]::IsNullOrWhiteSpace($outcomeReference) -or $outcomes.ContainsKey($outcomeReference)) {
            throw "Restricted fixture truth store has a missing or duplicate outcome reference."
        }

        $expectedCount = Get-PropertyValue -Object $outcome -Name "expectedSpeakerCount"
        $parsedCount = 0
        if ($null -eq $expectedCount -or -not [int]::TryParse([string]$expectedCount, [ref]$parsedCount) -or $parsedCount -lt 0 -or $parsedCount -gt 16) {
            throw "Restricted fixture truth store outcome '$outcomeReference' expectedSpeakerCount must be an integer from 0 through 16."
        }

        $outcomes[$outcomeReference] = $outcome
    }

    $reference = [string](Get-PropertyValue -Object $Fixture -Name "expectedOutcomeRef")
    if ([string]::IsNullOrWhiteSpace($reference) -or -not $outcomes.ContainsKey($reference)) {
        throw "Fixture '$([string](Get-PropertyValue -Object $Fixture -Name "id"))' has no approved expected outcome."
    }

    return $outcomes[$reference]
}

function Test-PathUnder {
    param(
        [string]$PathValue,
        [string]$RootValue
    )

    $fullPath = [System.IO.Path]::GetFullPath($PathValue).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $fullRoot = [System.IO.Path]::GetFullPath($RootValue).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    return $fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullRoot + [System.IO.Path]::AltDirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-AllowedOutputPath {
    param(
        [string]$PathValue,
        [string]$Description
    )

    $fullPath = [System.IO.Path]::GetFullPath($PathValue)
    $artifactsRoot = Join-Path $script:RepoRoot ".artifacts"
    $tempRoot = [System.IO.Path]::GetTempPath()

    if ((Test-PathUnder -PathValue $fullPath -RootValue $artifactsRoot) -or
        (Test-PathUnder -PathValue $fullPath -RootValue $tempRoot)) {
        return $fullPath
    }

    throw "$Description must write under .artifacts or the system temp directory: $fullPath"
}

function Assert-AllowlistedFixtureInput {
    param(
        [string]$PathValue,
        [string]$InputRoot,
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    $fullPath = [System.IO.Path]::GetFullPath($PathValue)
    if ([string]::IsNullOrWhiteSpace($InputRoot)) {
        return $fullPath
    }

    $fullInputRoot = Resolve-RequiredDirectory -PathValue $InputRoot -Description "Fixture inputRoot"
    if (-not (Test-PathUnder -PathValue $fullPath -RootValue $fullInputRoot)) {
        throw "$Description must stay under fixture inputRoot."
    }

    foreach ($forbiddenRoot in @($script:RepoRoot, $script:ResolvedReportsRoot, (Join-Path $env:LOCALAPPDATA "MeetingRecorder"))) {
        if (-not [string]::IsNullOrWhiteSpace($forbiddenRoot) -and (Test-PathUnder -PathValue $fullPath -RootValue $forbiddenRoot)) {
            throw "$Description cannot use source control, report output, or Meeting Recorder app data."
        }
    }

    $currentPath = $fullPath
    while (-not [string]::IsNullOrWhiteSpace($currentPath)) {
        $item = Get-Item -LiteralPath $currentPath -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Description cannot traverse a symlink or reparse point."
        }

        if ($currentPath.Equals($fullInputRoot, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $parent = Split-Path -Path $currentPath -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent.Equals($currentPath, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $currentPath = $parent
    }

    return $fullPath
}

function Get-SafeTempResultPath {
    param([string]$FixtureId)

    $safeId = if ([string]::IsNullOrWhiteSpace($FixtureId)) {
        [Guid]::NewGuid().ToString("N")
    }
    else {
        ($FixtureId -replace '[^A-Za-z0-9_.-]', '-')
    }

    return Join-Path ([System.IO.Path]::GetTempPath()) ("meeting-recorder-fixture-{0}-{1}.json" -f $safeId, [Guid]::NewGuid().ToString("N"))
}

function Get-SafeTempProbeRoot {
    param([string]$FixtureId)

    $safeId = if ([string]::IsNullOrWhiteSpace($FixtureId)) {
        [Guid]::NewGuid().ToString('N')
    }
    else {
        ($FixtureId -replace '[^A-Za-z0-9_.-]', '-')
    }

    return Join-Path ([System.IO.Path]::GetTempPath()) ("meeting-recorder-fixture-probe-{0}-{1}" -f $safeId, [Guid]::NewGuid().ToString('N'))
}

function Get-FileSha256 {
    param([string]$PathValue)

    $stream = [System.IO.File]::OpenRead($PathValue)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return [System.BitConverter]::ToString($sha256.ComputeHash($stream)).Replace("-", "")
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-SafeReportMessage {
    param([string]$Message)

    if ([string]::IsNullOrWhiteSpace($Message)) {
        return $Message
    }

    # Fixture paths often identify a private meeting or a local user.  Keep a
    # stable diagnostic class, but never copy the original path to an artifact.
    if ($Message -match '(?i)(?:[a-z]:\\|/)[^\r\n]*') {
        return "Fixture input could not be resolved. See the local runner output."
    }

    return $Message
}

function Get-SafeDiarizationState {
    param($State)

    # Processing-worker JSON serializes StageExecutionState as either its
    # numeric value or enum name.  Normalize only known values so reports keep
    # a useful diagnostic without carrying free-form provider output.
    switch ([string]$State) {
        "0" { return "not_started" }
        "NotStarted" { return "not_started" }
        "1" { return "queued" }
        "Queued" { return "queued" }
        "2" { return "running" }
        "Running" { return "running" }
        "3" { return "succeeded" }
        "Succeeded" { return "succeeded" }
        "4" { return "failed" }
        "Failed" { return "failed" }
        "5" { return "skipped" }
        "Skipped" { return "skipped" }
        default { return "unknown" }
    }
}

function Get-SafeDiarizationFailureKind {
    param(
        [string]$State,
        [string]$Message
    )

    if ($State -ne "failed") {
        return "not_failed"
    }

    if ($Message -match 'helper failed \((audio_preparation|native_inference|runtime_status)_(file_not_found|directory_not_found|access_denied|not_supported|invalid_operation|com|io|cancelled|other|unknown)\)') {
        return "helper_" + $Matches[1] + "_" + $Matches[2]
    }

    # Classify in memory only.  Do not persist provider text: it may identify
    # a private path, a machine account, or an approved fixture.
    if ($Message -match '(?i)onnx|model|asset') { return "model_or_asset" }
    if ($Message -match '(?i)directml|cuda|gpu|accelerat') { return "acceleration" }
    if ($Message -match '(?i)timeout|cancel') { return "timeout_or_cancel" }
    if ($Message -match '(?i)file|path|directory|not found|missing') { return "input_or_file" }
    if ($Message -match '(?i)helper') { return "helper_execution" }
    return "provider_or_processing"
}

function Get-ArtifactHashes {
    param(
        [string[]]$Paths,
        [string]$InputRoot
    )

    $hashes = @()
    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        $resolved = Resolve-CatalogPathValue -PathValue $path -Description "Protected artifact" -Required
        $resolved = Assert-AllowlistedFixtureInput -PathValue $resolved -InputRoot $InputRoot -Description "Protected artifact"
        $hashes += [pscustomobject]@{
            path = $resolved
            hash = Get-FileSha256 -PathValue $resolved
        }
    }

    return $hashes
}

function Get-TranscriptMetrics {
    param([string]$TranscriptJsonPath)

    if ([string]::IsNullOrWhiteSpace($TranscriptJsonPath) -or -not (Test-Path -LiteralPath $TranscriptJsonPath -PathType Leaf)) {
        return [pscustomobject]@{
            segmentCount = $null
            rawSpeakerLabelCount = $null
            speakerRunCount = $null
        }
    }

    $document = Get-Content -LiteralPath $TranscriptJsonPath -Raw | ConvertFrom-Json
    $segments = @($document.segments)
    $labels = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $speakerRunCount = 0
    $lastLabel = $null
    foreach ($segment in $segments) {
        $label = if ($segment.PSObject.Properties.Name -contains "speakerLabel" -and -not [string]::IsNullOrWhiteSpace([string]$segment.speakerLabel)) {
            [string]$segment.speakerLabel
        }
        elseif ($segment.PSObject.Properties.Name -contains "speakerId" -and -not [string]::IsNullOrWhiteSpace([string]$segment.speakerId)) {
            [string]$segment.speakerId
        }
        else {
            "(unlabeled)"
        }

        if ($label -ne "(unlabeled)") {
            [void]$labels.Add($label)
        }

        if ($null -eq $lastLabel -or $lastLabel -ne $label) {
            $speakerRunCount++
            $lastLabel = $label
        }
    }

    return [pscustomobject]@{
        segmentCount = $segments.Count
        rawSpeakerLabelCount = $labels.Count
        speakerRunCount = $speakerRunCount
    }
}

function Get-SpeakerCountStatus {
    param(
        [Nullable[int]]$ExpectedSpeakerCount,
        [Nullable[int]]$DetectedSpeakerCount,
        [string]$FallbackStatus
    )

    if ($FallbackStatus -in @("skipped", "missing_data", "failed", "dry_run")) {
        return $FallbackStatus
    }

    if ($null -eq $ExpectedSpeakerCount) {
        return "not_asserted"
    }

    if ($null -eq $DetectedSpeakerCount) {
        return "missing_data"
    }

    if ($DetectedSpeakerCount -eq $ExpectedSpeakerCount) {
        return "pass"
    }

    if ($DetectedSpeakerCount -gt $ExpectedSpeakerCount) {
        return "too_many"
    }

    return "too_few"
}

function New-BaseResult {
    param($Fixture)

    $expectedSpeakerCount = Get-PropertyValue -Object $Fixture -Name "expectedSpeakerCount"
    return [ordered]@{
        fixtureId = [string](Get-PropertyValue -Object $Fixture -Name "id")
        category = [string](Get-PropertyValue -Object $Fixture -Name "category")
        mode = [string](Get-PropertyValue -Object $Fixture -Name "mode")
        enabled = [bool](Get-PropertyValue -Object $Fixture -Name "enabled" -DefaultValue $true)
        status = "pending"
        skipReason = $null
        elapsedMilliseconds = 0
        expectedSpeakerCount = if ($null -eq $expectedSpeakerCount -or [string]::IsNullOrWhiteSpace([string]$expectedSpeakerCount)) { $null } else { [int]$expectedSpeakerCount }
        detectedSpeakerCount = $null
        speakerCountStatus = "not_run"
        rawSpeakerLabelCount = $null
        contiguousSpeakerRunCount = $null
        segmentCount = $null
        voiceSampleCount = $null
        diarizationState = $null
        diarizationFailureKind = $null
        speakerNameMetrics = [ordered]@{
            enabled = [bool](Get-PropertyValue -Object $Fixture -Name "enableSpeakerNameMatching" -DefaultValue $false)
            expectedNameCount = @(ConvertTo-Array (Get-PropertyValue -Object $Fixture -Name "expectedSpeakerNames")).Count
            detectedNameCount = $null
            missingExpectedNameCount = $null
            unexpectedRealNameCount = $null
            suggestionCount = $null
            autoApplyCount = $null
            falseAutoApplyCount = $null
            unknownSpeakerCount = $null
            status = "not_run"
        }
        artifactSafety = [ordered]@{
            protectedArtifactCount = 0
            allUnchanged = $true
        }
        dispatch = [ordered]@{
            script = $null
            dryRun = [bool]$DryRun
        }
        message = $null
    }
}

function Apply-ExpectedOutcome {
    param(
        [System.Collections.IDictionary]$Result,
        $Fixture,
        $Outcome
    )

    $expectedSpeakerCount = if ($null -eq $Outcome) {
        Get-PropertyValue -Object $Fixture -Name "expectedSpeakerCount"
    }
    else {
        Get-PropertyValue -Object $Outcome -Name "expectedSpeakerCount"
    }
    $expectedNames = if ($null -eq $Outcome) {
        @(ConvertTo-Array (Get-PropertyValue -Object $Fixture -Name "expectedSpeakerNames"))
    }
    else {
        @(ConvertTo-Array (Get-PropertyValue -Object $Outcome -Name "expectedSpeakerNames"))
    }

    $Result.expectedSpeakerCount = if ($null -eq $expectedSpeakerCount -or [string]::IsNullOrWhiteSpace([string]$expectedSpeakerCount)) { $null } else { [int]$expectedSpeakerCount }
    $Result.speakerNameMetrics.expectedNameCount = $expectedNames.Count
    return $expectedNames
}

function Validate-Fixture {
    param($Fixture)

    $id = [string](Get-PropertyValue -Object $Fixture -Name "id")
    if ([string]::IsNullOrWhiteSpace($id)) {
        throw "Fixture id is required."
    }

    if ($script:CatalogSchemaVersion -ge 2) {
        foreach ($requiredProperty in @("version", "contentHash", "category", "scenarioTags", "consentRecord", "classification", "owner", "expiresAtUtc", "expectedOutcomeRef", "inputRoot")) {
            if (-not (Test-PropertyPresent -Object $Fixture -Name $requiredProperty) -or [string]::IsNullOrWhiteSpace([string](Get-PropertyValue -Object $Fixture -Name $requiredProperty))) {
                throw "Fixture '$id' requires $requiredProperty in catalog schema version 2."
            }
        }

        if ((Test-PropertyPresent -Object $Fixture -Name "expectedSpeakerCount") -or (Test-PropertyPresent -Object $Fixture -Name "expectedSpeakerNames")) {
            throw "Fixture '$id' must keep expected outcomes in the restricted truth store, not catalog schema version 2."
        }

        if ([string](Get-PropertyValue -Object $Fixture -Name "contentHash") -notmatch '^[A-Fa-f0-9]{64}$') {
            throw "Fixture '$id' contentHash must be a SHA-256 hex value."
        }

        $expiry = [DateTime]::MinValue
        if (-not [DateTime]::TryParse([string](Get-PropertyValue -Object $Fixture -Name "expiresAtUtc"), [ref]$expiry)) {
            throw "Fixture '$id' expiresAtUtc must be a valid date."
        }
    }

    $mode = [string](Get-PropertyValue -Object $Fixture -Name "mode")
    if ($mode -notin @("stored-turn", "full-audio")) {
        throw "Fixture '$id' has invalid mode '$mode'. Use 'stored-turn' or 'full-audio'."
    }

    $expectedSpeakerCount = Get-PropertyValue -Object $Fixture -Name "expectedSpeakerCount"
    if ($null -ne $expectedSpeakerCount -and -not [string]::IsNullOrWhiteSpace([string]$expectedSpeakerCount)) {
        $parsedCount = 0
        if (-not [int]::TryParse([string]$expectedSpeakerCount, [ref]$parsedCount) -or $parsedCount -lt 0 -or $parsedCount -gt 16) {
            throw "Fixture '$id' expectedSpeakerCount must be an integer from 0 through 16."
        }
    }

    $expectedNames = @(ConvertTo-Array (Get-PropertyValue -Object $Fixture -Name "expectedSpeakerNames"))
    $enableSpeakerNameMatching = [bool](Get-PropertyValue -Object $Fixture -Name "enableSpeakerNameMatching" -DefaultValue $false)
    if ($expectedNames.Count -gt 0 -and -not $enableSpeakerNameMatching) {
        throw "Fixture '$id' supplies expected speaker names but enableSpeakerNameMatching is false."
    }

    $probeRoot = [string](Get-PropertyValue -Object $Fixture -Name "probeRoot")
    if (-not [string]::IsNullOrWhiteSpace($probeRoot)) {
        [void](Assert-AllowedOutputPath -PathValue $probeRoot -Description "Fixture '$id' probeRoot")
    }
}

function Resolve-FixtureInputs {
    param($Fixture)

    $mode = [string](Get-PropertyValue -Object $Fixture -Name "mode")
    $inputRoot = [string](Get-PropertyValue -Object $Fixture -Name "inputRoot")
    $transcriptionSnapshotPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "transcriptionSnapshotPath")) -Description "Fixture transcription snapshot" -Required:($mode -eq "full-audio")
    $transcriptJsonPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "transcriptJsonPath")) -Description "Fixture transcript JSON" -Required:($mode -eq "stored-turn")
    if ([string]::IsNullOrWhiteSpace($transcriptJsonPath)) {
        $transcriptJsonPath = $transcriptionSnapshotPath
    }
    $manifestPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "manifestPath")) -Description "Fixture manifest" -Required:($mode -eq "stored-turn" -or $mode -eq "full-audio")
    $transcriptMarkdownPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "transcriptMarkdownPath")) -Description "Fixture transcript Markdown"

    $transcriptJsonPath = Assert-AllowlistedFixtureInput -PathValue $transcriptJsonPath -InputRoot $inputRoot -Description "Fixture transcript JSON"
    $manifestPath = Assert-AllowlistedFixtureInput -PathValue $manifestPath -InputRoot $inputRoot -Description "Fixture manifest"
    $transcriptMarkdownPath = Assert-AllowlistedFixtureInput -PathValue $transcriptMarkdownPath -InputRoot $inputRoot -Description "Fixture transcript Markdown"
    $audioPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "audioPath")) -Description "Fixture audio"
    $configPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "configPath")) -Description "Fixture config"

    return [pscustomobject]@{
        inputRoot = $inputRoot
        transcriptJsonPath = $transcriptJsonPath
        manifestPath = $manifestPath
        transcriptMarkdownPath = $transcriptMarkdownPath
        audioPath = Assert-AllowlistedFixtureInput -PathValue $audioPath -InputRoot $inputRoot -Description "Fixture audio"
        transcriptionSnapshotPath = Assert-AllowlistedFixtureInput -PathValue $transcriptionSnapshotPath -InputRoot $inputRoot -Description "Fixture transcription snapshot"
        configPath = Assert-AllowlistedFixtureInput -PathValue $configPath -InputRoot $inputRoot -Description "Fixture config"
        diarizationAssetPath = [string](Get-PropertyValue -Object $Fixture -Name "diarizationAssetPath")
        probeRoot = [string](Get-PropertyValue -Object $Fixture -Name "probeRoot")
        protectedArtifactPaths = @(ConvertTo-Array (Get-PropertyValue -Object $Fixture -Name "protectedArtifactPaths"))
    }
}

function Invoke-Fixture {
    param(
        $Fixture,
        [string]$StoredReplayScript,
        [string]$FullAudioScript
    )

    $result = New-BaseResult -Fixture $Fixture
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        Validate-Fixture -Fixture $Fixture

        if ($script:CatalogSchemaVersion -ge 2) {
            $expiry = [DateTime]::Parse([string](Get-PropertyValue -Object $Fixture -Name "expiresAtUtc")).ToUniversalTime()
            if ($expiry -le [DateTime]::UtcNow) {
                $result.status = "skipped"
                $result.skipReason = "Fixture consent or retention approval expired."
                $result.speakerCountStatus = "skipped"
                $result.speakerNameMetrics.status = "skipped"
                return [pscustomobject]$result
            }
        }

        if (-not $result.enabled) {
            $result.status = "skipped"
            $result.skipReason = [string](Get-PropertyValue -Object $Fixture -Name "disabledReason" -DefaultValue "Fixture is disabled.")
            $result.speakerCountStatus = "skipped"
            $result.speakerNameMetrics.status = "skipped"
            return [pscustomobject]$result
        }

        $inputs = $null
        try {
            $inputs = Resolve-FixtureInputs -Fixture $Fixture
        }
        catch {
            $result.status = "missing_data"
            $result.message = $_.Exception.Message
            $result.speakerCountStatus = "missing_data"
            $result.speakerNameMetrics.status = "missing_data"
            return [pscustomobject]$result
        }

        $artifactHashesBefore = @(Get-ArtifactHashes -Paths $inputs.protectedArtifactPaths -InputRoot $inputs.inputRoot)
        $transcriptMetrics = Get-TranscriptMetrics -TranscriptJsonPath $inputs.transcriptJsonPath
        $result.segmentCount = $transcriptMetrics.segmentCount
        $result.rawSpeakerLabelCount = $transcriptMetrics.rawSpeakerLabelCount
        $result.contiguousSpeakerRunCount = $transcriptMetrics.speakerRunCount
        $result.artifactSafety.protectedArtifactCount = $artifactHashesBefore.Count

        if ($result.mode -eq "stored-turn") {
            $result.dispatch.script = "Test-DiarizationFixture.ps1"
        }
        else {
            $result.dispatch.script = "Test-DiarizationFullAudioFixture.ps1"
        }

        if ($DryRun) {
            $expectedOutcome = Get-TruthOutcome -Fixture $Fixture
            $expectedNames = @(Apply-ExpectedOutcome -Result $result -Fixture $Fixture -Outcome $expectedOutcome)
            if ($expectedNames.Count -gt 0 -and -not $result.speakerNameMetrics.enabled) {
                throw "Fixture '$($result.fixtureId)' supplies expected speaker names but enableSpeakerNameMatching is false."
            }
            $result.status = "dry_run"
            $result.speakerCountStatus = Get-SpeakerCountStatus -ExpectedSpeakerCount $result.expectedSpeakerCount -DetectedSpeakerCount $null -FallbackStatus "dry_run"
            $result.speakerNameMetrics.status = "dry_run"
            return [pscustomobject]$result
        }

        $childResultPath = Get-SafeTempResultPath -FixtureId $result.fixtureId
        $probeRootToCleanup = $null
        try {
            if ($result.mode -eq "stored-turn") {
                $arguments = @{
                    JsonPath = $inputs.transcriptJsonPath
                    ManifestPath = $inputs.manifestPath
                    Configuration = $Configuration
                    ResultPath = $childResultPath
                }
                if ($NoBuild) {
                    $arguments.NoBuild = $true
                }

                $null = & $StoredReplayScript @arguments | Out-String
                $childResult = Get-Content -LiteralPath $childResultPath -Raw | ConvertFrom-Json
                $result.detectedSpeakerCount = [int]$childResult.MergedSpeakerCount
                $result.voiceSampleCount = [int]$childResult.VoiceSampleCount
            }
            else {
                $arguments = @{
                    ManifestPath = $inputs.manifestPath
                    Configuration = $Configuration
                    ResultPath = $childResultPath
                }
                if ($inputs.configPath) { $arguments.ConfigPath = $inputs.configPath }
                if ($inputs.audioPath) { $arguments.AudioPath = $inputs.audioPath }
                if ($inputs.transcriptionSnapshotPath) { $arguments.TranscriptionSnapshotPath = $inputs.transcriptionSnapshotPath }
                $probeRootToCleanup = Get-SafeTempProbeRoot -FixtureId $result.fixtureId
                $arguments.ProbeRoot = $probeRootToCleanup
                if (-not [string]::IsNullOrWhiteSpace($inputs.diarizationAssetPath)) { $arguments.DiarizationAssetPath = $inputs.diarizationAssetPath }
                if ([bool](Get-PropertyValue -Object $Fixture -Name "enableSpeakerNameMatching" -DefaultValue $false)) {
                    $arguments.EnableSpeakerNameMatching = $true
                }
                $speakerLabelingProfile = [string](Get-PropertyValue -Object $Fixture -Name "speakerLabelingProfile" -DefaultValue "Configured")
                if (-not [string]::IsNullOrWhiteSpace($speakerLabelingProfile)) { $arguments.SpeakerLabelingProfile = $speakerLabelingProfile }
                $acceleration = [string](Get-PropertyValue -Object $Fixture -Name "accelerationPreference" -DefaultValue "CpuOnly")
                if (-not [string]::IsNullOrWhiteSpace($acceleration)) { $arguments.AccelerationPreference = $acceleration }

                $null = & $FullAudioScript @arguments | Out-String
                $childResult = Get-Content -LiteralPath $childResultPath -Raw | ConvertFrom-Json
                $result.detectedSpeakerCount = [int]$childResult.SpeakerCount
                $result.voiceSampleCount = [int]$childResult.VoiceSampleCount
                # State is a fixed stage enum.  Keep it, but never persist the
                # accompanying free-form message because it can contain private
                # local paths or provider diagnostics.
                $result.diarizationState = Get-SafeDiarizationState -State $childResult.DiarizationState
                $result.diarizationFailureKind = Get-SafeDiarizationFailureKind -State $result.diarizationState -Message ([string]$childResult.DiarizationMessage)
                $detectedNames = @(ConvertTo-Array $childResult.SpeakerNames)
                $result.speakerNameMetrics.detectedNameCount = $detectedNames.Count
                $result.speakerNameMetrics.missingExpectedNameCount = @($expectedNames | Where-Object {
                        $expectedName = $_
                        -not ($detectedNames | Where-Object { $_ -eq $expectedName })
                    }).Count
                $result.speakerNameMetrics.unexpectedRealNameCount = if ($expectedNames.Count -eq 0) {
                    0
                }
                else {
                    @($detectedNames | Where-Object {
                            $detectedName = $_
                            -not ($expectedNames | Where-Object { $_ -eq $detectedName })
                        }).Count
                }
            }
        }
        finally {
            if (Test-Path -LiteralPath $childResultPath -PathType Leaf) {
                Remove-Item -LiteralPath $childResultPath -Force
            }
            if (-not [string]::IsNullOrWhiteSpace($probeRootToCleanup) -and
                (Test-Path -LiteralPath $probeRootToCleanup -PathType Container) -and
                (Test-PathUnder -PathValue $probeRootToCleanup -RootValue ([System.IO.Path]::GetTempPath()))) {
                Remove-Item -LiteralPath $probeRootToCleanup -Recurse -Force
            }
        }

        $artifactResults = @()
        foreach ($before in $artifactHashesBefore) {
            $afterHash = Get-FileSha256 -PathValue $before.path
            $artifactResults += ($before.hash -eq $afterHash)
        }
        $result.artifactSafety.allUnchanged = @($artifactResults | Where-Object { -not $_ }).Count -eq 0

        $expectedOutcome = Get-TruthOutcome -Fixture $Fixture
        $expectedNames = @(Apply-ExpectedOutcome -Result $result -Fixture $Fixture -Outcome $expectedOutcome)
        if ($expectedNames.Count -gt 0 -and -not $result.speakerNameMetrics.enabled) {
            throw "Fixture '$($result.fixtureId)' supplies expected speaker names but enableSpeakerNameMatching is false."
        }

        $result.speakerCountStatus = Get-SpeakerCountStatus -ExpectedSpeakerCount $result.expectedSpeakerCount -DetectedSpeakerCount $result.detectedSpeakerCount -FallbackStatus "completed"
        $result.status = if ($result.speakerCountStatus -eq "pass" -or $result.speakerCountStatus -eq "not_asserted") { "completed" } else { $result.speakerCountStatus }
        if (-not $result.artifactSafety.allUnchanged) {
            $result.status = "failed"
            $result.message = "A protected artifact changed during fixture execution."
        }

        if ($result.speakerNameMetrics.enabled) {
            if ($result.speakerNameMetrics.expectedNameCount -gt 0) {
                $result.speakerNameMetrics.status = if ($result.speakerNameMetrics.missingExpectedNameCount -eq 0 -and $result.speakerNameMetrics.unexpectedRealNameCount -eq 0) {
                    "pass"
                }
                else {
                    "failed"
                }
            }
            else {
                $result.speakerNameMetrics.status = "not_asserted"
            }
        }
        else {
            $result.speakerNameMetrics.status = "disabled"
        }
    }
    catch {
        $result.status = "failed"
        $result.message = Get-SafeReportMessage -Message $_.Exception.Message
        $result.speakerCountStatus = "failed"
        if ($result.speakerNameMetrics.status -eq "not_run") {
            $result.speakerNameMetrics.status = "failed"
        }
    }
    finally {
        $stopwatch.Stop()
        $result.elapsedMilliseconds = [int64]$stopwatch.ElapsedMilliseconds
    }

    return [pscustomobject]$result
}

$script:RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).ProviderPath
if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    $CatalogPath = Join-Path $script:RepoRoot ".artifacts\diarization-fixtures\fixture-catalog.local.json"
}

$resolvedCatalogPath = Resolve-RequiredFile -PathValue $CatalogPath -Description "Fixture catalog"
if ([string]::IsNullOrWhiteSpace($ReportsRoot)) {
    $ReportsRoot = Join-Path $script:RepoRoot ".artifacts\diarization-fixtures\reports"
}

$resolvedReportsRoot = Assert-AllowedOutputPath -PathValue $ReportsRoot -Description "ReportsRoot"
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $resolvedReportsRoot ("diarization-fixture-catalog-{0}.json" -f (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss"))
}

$resolvedReportPath = Assert-AllowedOutputPath -PathValue $ReportPath -Description "ReportPath"
New-Item -ItemType Directory -Path (Split-Path -Path $resolvedReportPath -Parent) -Force | Out-Null

$catalog = Get-Content -LiteralPath $resolvedCatalogPath -Raw | ConvertFrom-Json
$script:CatalogSchemaVersion = [int](Get-PropertyValue -Object $catalog -Name "schemaVersion" -DefaultValue 1)
if ($script:CatalogSchemaVersion -notin @(1, 2)) {
    throw "Fixture catalog schemaVersion must be 1 or 2."
}

$script:ResolvedTruthStorePath = $null
if ($script:CatalogSchemaVersion -ge 2) {
    if ([string]::IsNullOrWhiteSpace($TruthStorePath)) {
        throw "Catalog schema version 2 requires -TruthStorePath outside source control and report output."
    }

    $script:ResolvedTruthStorePath = Resolve-RequiredFile -PathValue $TruthStorePath -Description "Restricted fixture truth store"
    if (Test-PathUnder -PathValue $script:ResolvedTruthStorePath -RootValue $script:RepoRoot) {
        throw "Restricted fixture truth store must stay outside source control."
    }
}

$fixtures = if ($catalog.PSObject.Properties.Name -contains "fixtures") {
    @(ConvertTo-Array $catalog.fixtures)
}
else {
    @(ConvertTo-Array $catalog)
}

if ($FixtureId.Count -gt 0) {
    $fixtureIdSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($id in $FixtureId) { [void]$fixtureIdSet.Add($id) }
    $fixtures = @($fixtures | Where-Object { $fixtureIdSet.Contains([string](Get-PropertyValue -Object $_ -Name "id")) })
}

if ($Category.Count -gt 0) {
    $categorySet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $Category) { [void]$categorySet.Add($item) }
    $fixtures = @($fixtures | Where-Object { $categorySet.Contains([string](Get-PropertyValue -Object $_ -Name "category")) })
}

if (-not $IncludeDisabled) {
    $fixtures = @($fixtures | Where-Object { [bool](Get-PropertyValue -Object $_ -Name "enabled" -DefaultValue $true) })
}

$storedReplayScript = Join-Path $script:RepoRoot "scripts\Test-DiarizationFixture.ps1"
$fullAudioScript = Join-Path $script:RepoRoot "scripts\Test-DiarizationFullAudioFixture.ps1"
$results = @()
$runStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
foreach ($fixture in $fixtures) {
    $result = Invoke-Fixture -Fixture $fixture -StoredReplayScript $storedReplayScript -FullAudioScript $fullAudioScript
    $results += $result

    Write-Output ("{0}: {1}" -f $result.fixtureId, $result.status)
    if ($result.status -eq "failed" -and -not [string]::IsNullOrWhiteSpace($result.message)) {
        Write-Output "  Failure: metadata-only fixture execution failed."
    }
}

$runStopwatch.Stop()
$summary = [ordered]@{
    total = $results.Count
    completed = @($results | Where-Object { $_.status -eq "completed" }).Count
    passed = @($results | Where-Object { $_.speakerCountStatus -eq "pass" }).Count
    tooFew = @($results | Where-Object { $_.speakerCountStatus -eq "too_few" }).Count
    tooMany = @($results | Where-Object { $_.speakerCountStatus -eq "too_many" }).Count
    missingData = @($results | Where-Object { $_.status -eq "missing_data" }).Count
    skipped = @($results | Where-Object { $_.status -eq "skipped" }).Count
    failed = @($results | Where-Object { $_.status -eq "failed" }).Count
    dryRun = @($results | Where-Object { $_.status -eq "dry_run" }).Count
    elapsedMilliseconds = [int64]$runStopwatch.ElapsedMilliseconds
}

$report = [ordered]@{
    schemaVersion = 2
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    catalogSha256 = Get-FileSha256 -PathValue $resolvedCatalogPath
    dryRun = [bool]$DryRun
    calibration = [ordered]@{
        runName = if ([string]::IsNullOrWhiteSpace($CalibrationRunName)) { "current-defaults" } else { $CalibrationRunName }
        parameters = if ([string]::IsNullOrWhiteSpace($CalibrationParametersJson)) { $null } else { $CalibrationParametersJson | ConvertFrom-Json }
    }
    filters = [ordered]@{
        fixtureId = $FixtureId
        category = $Category
        includeDisabled = [bool]$IncludeDisabled
    }
    truthStoreSha256 = if ($script:ResolvedTruthStorePath) { Get-FileSha256 -PathValue $script:ResolvedTruthStorePath } else { $null }
    summary = $summary
    results = $results
}

$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $resolvedReportPath -Encoding UTF8

Write-Output ""
Write-Output "Diarization fixture catalog summary:"
Write-Output "  Fixtures: $($summary.total)"
Write-Output "  Completed: $($summary.completed)"
Write-Output "  Passed: $($summary.passed)"
Write-Output "  Too few: $($summary.tooFew)"
Write-Output "  Too many: $($summary.tooMany)"
Write-Output "  Missing data: $($summary.missingData)"
Write-Output "  Skipped: $($summary.skipped)"
Write-Output "  Failed: $($summary.failed)"
if ($DryRun) {
    Write-Output "  Dry run: $($summary.dryRun)"
}
Write-Output "  Report: $resolvedReportPath"

if ($summary.failed -gt 0) {
    throw "$($summary.failed) fixture(s) failed. See report: $resolvedReportPath"
}
