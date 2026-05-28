param(
    [string]$CatalogPath,

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

function Get-ArtifactHashes {
    param([string[]]$Paths)

    $hashes = @()
    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        $resolved = Resolve-CatalogPathValue -PathValue $path -Description "Protected artifact" -Required
        $hashes += [pscustomobject]@{
            path = $resolved
            hash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash
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
        title = [string](Get-PropertyValue -Object $Fixture -Name "title")
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
            tempOutputPath = $null
            artifacts = @()
        }
        dispatch = [ordered]@{
            script = $null
            dryRun = [bool]$DryRun
        }
        message = $null
    }
}

function Validate-Fixture {
    param($Fixture)

    $id = [string](Get-PropertyValue -Object $Fixture -Name "id")
    if ([string]::IsNullOrWhiteSpace($id)) {
        throw "Fixture id is required."
    }

    $mode = [string](Get-PropertyValue -Object $Fixture -Name "mode")
    if ($mode -notin @("stored-turn", "full-audio")) {
        throw "Fixture '$id' has invalid mode '$mode'. Use 'stored-turn' or 'full-audio'."
    }

    $expectedSpeakerCount = Get-PropertyValue -Object $Fixture -Name "expectedSpeakerCount"
    if ($null -ne $expectedSpeakerCount -and -not [string]::IsNullOrWhiteSpace([string]$expectedSpeakerCount)) {
        $parsedCount = 0
        if (-not [int]::TryParse([string]$expectedSpeakerCount, [ref]$parsedCount) -or $parsedCount -lt 1 -or $parsedCount -gt 16) {
            throw "Fixture '$id' expectedSpeakerCount must be an integer from 1 through 16."
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
    $transcriptJsonPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "transcriptJsonPath")) -Description "Fixture transcript JSON" -Required
    $manifestPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "manifestPath")) -Description "Fixture manifest" -Required:($mode -eq "stored-turn" -or $mode -eq "full-audio")
    $transcriptMarkdownPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "transcriptMarkdownPath")) -Description "Fixture transcript Markdown"

    return [pscustomobject]@{
        transcriptJsonPath = $transcriptJsonPath
        manifestPath = $manifestPath
        transcriptMarkdownPath = $transcriptMarkdownPath
        audioPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "audioPath")) -Description "Fixture audio"
        transcriptionSnapshotPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "transcriptionSnapshotPath")) -Description "Fixture transcription snapshot"
        configPath = Resolve-CatalogPathValue -PathValue ([string](Get-PropertyValue -Object $Fixture -Name "configPath")) -Description "Fixture config"
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

        $artifactHashesBefore = @(Get-ArtifactHashes -Paths $inputs.protectedArtifactPaths)
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
            $result.status = "dry_run"
            $result.speakerCountStatus = Get-SpeakerCountStatus -ExpectedSpeakerCount $result.expectedSpeakerCount -DetectedSpeakerCount $null -FallbackStatus "dry_run"
            $result.speakerNameMetrics.status = "dry_run"
            return [pscustomobject]$result
        }

        $childResultPath = Get-SafeTempResultPath -FixtureId $result.fixtureId
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
                if (-not [string]::IsNullOrWhiteSpace($inputs.probeRoot)) { $arguments.ProbeRoot = $inputs.probeRoot }
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
                $result.artifactSafety.tempOutputPath = [string]$childResult.ProbeRoot
                $detectedNames = @(ConvertTo-Array $childResult.SpeakerNames)
                $expectedNames = @(ConvertTo-Array (Get-PropertyValue -Object $Fixture -Name "expectedSpeakerNames"))
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
        }

        $artifactResults = @()
        foreach ($before in $artifactHashesBefore) {
            $afterHash = (Get-FileHash -LiteralPath $before.path -Algorithm SHA256).Hash
            $artifactResults += [ordered]@{
                path = $before.path
                beforeHash = $before.hash
                afterHash = $afterHash
                unchanged = $before.hash -eq $afterHash
            }
        }
        $result.artifactSafety.artifacts = $artifactResults
        $result.artifactSafety.allUnchanged = @($artifactResults | Where-Object { -not $_.unchanged }).Count -eq 0

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
        $result.message = $_.Exception.Message
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

    $title = if ([string]::IsNullOrWhiteSpace($result.title)) { $result.fixtureId } else { $result.title }
    Write-Output ("{0}: {1} ({2})" -f $result.fixtureId, $result.status, $title)
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
    schemaVersion = 1
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    catalogPath = $resolvedCatalogPath
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
