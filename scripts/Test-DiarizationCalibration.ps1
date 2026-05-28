param(
    [string]$CatalogPath,

    [string]$CandidatePath,

    [string[]]$CandidateName = @(),

    [switch]$IncludeDisabledCandidates,

    [string[]]$FixtureId = @(),

    [string[]]$Category = @(),

    [switch]$IncludeDisabled,

    [switch]$DryRun,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$NoBuild,

    [string]$ReportsRoot,

    [string]$ReportPath
)

$ErrorActionPreference = "Stop"

$AllowedCalibrationVariables = @(
    "MEETING_RECORDER_DIARIZATION_DEFAULT_THRESHOLD",
    "MEETING_RECORDER_DIARIZATION_COLLAPSED_THRESHOLDS",
    "MEETING_RECORDER_DIARIZATION_OVERSEGMENTED_THRESHOLDS",
    "MEETING_RECORDER_DIARIZATION_MIN_SPEAKER_DURATION_SECONDS",
    "MEETING_RECORDER_DIARIZATION_MAX_MIN_SPEAKER_DURATION_SECONDS",
    "MEETING_RECORDER_DIARIZATION_MIN_SPEAKER_DURATION_SHARE",
    "MEETING_RECORDER_DIARIZATION_MAX_PREFERRED_SPEAKER_COUNT",
    "MEETING_RECORDER_SPEAKER_CLUSTER_TINY_MAX_SECONDS",
    "MEETING_RECORDER_SPEAKER_CLUSTER_SMALL_MAX_SECONDS",
    "MEETING_RECORDER_SPEAKER_CLUSTER_SMALL_SHARE",
    "MEETING_RECORDER_SPEAKER_CLUSTER_HIGH_CONFIDENCE_THRESHOLD",
    "MEETING_RECORDER_SPEAKER_CLUSTER_SMALL_THRESHOLD",
    "MEETING_RECORDER_SPEAKER_NAME_AUTO_APPLY_CONFIDENCE_THRESHOLD",
    "MEETING_RECORDER_SPEAKER_NAME_SUGGESTION_CONFIDENCE_THRESHOLD",
    "MEETING_RECORDER_SPEAKER_NAME_MATCH_MARGIN_THRESHOLD",
    "MEETING_RECORDER_SPEAKER_NAME_MIN_AUTO_APPLY_PROFILE_SAMPLES",
    "MEETING_RECORDER_SPEAKER_NAME_MIN_AUTO_APPLY_SPEECH_SECONDS")

$ProtectedCategories = @(
    "two-speaker",
    "one-speaker",
    "three-plus-speaker",
    "short-call",
    "noisy-similar-voices")

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

function Resolve-RequiredFile {
    param(
        [string]$PathValue,
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "$Description path is required."
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($PathValue)
    if (-not [System.IO.Path]::IsPathRooted($expanded)) {
        $expanded = Join-Path $script:RepoRoot $expanded
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($expanded)
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$Description path is not a file: $resolvedPath"
    }

    return $resolvedPath
}

function Get-SafeFileNamePart {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [Guid]::NewGuid().ToString("N")
    }

    return ($Value -replace '[^A-Za-z0-9_.-]', '-')
}

function ConvertTo-EnvironmentMap {
    param($EnvironmentObject)

    $map = [ordered]@{}
    if ($null -eq $EnvironmentObject) {
        return $map
    }

    foreach ($property in $EnvironmentObject.PSObject.Properties) {
        if ($AllowedCalibrationVariables -notcontains $property.Name) {
            throw "Unsupported calibration variable '$($property.Name)'."
        }

        if ($property.Value -is [System.Array]) {
            $map[$property.Name] = (@($property.Value) -join ",")
        }
        else {
            $map[$property.Name] = [string]$property.Value
        }
    }

    return $map
}

function Get-CandidateDefinitions {
    param([string]$ResolvedCandidatePath)

    $baseline = [pscustomobject]@{
        name = "baseline-current-defaults"
        description = "Current production defaults with no calibration environment overrides."
        enabled = $true
        isBaseline = $true
        environment = [pscustomobject]@{}
    }

    $definitions = @($baseline)
    if ([string]::IsNullOrWhiteSpace($ResolvedCandidatePath)) {
        return $definitions
    }

    $document = Get-Content -LiteralPath $ResolvedCandidatePath -Raw | ConvertFrom-Json
    foreach ($candidate in @(ConvertTo-Array (Get-PropertyValue -Object $document -Name "candidates"))) {
        $name = [string](Get-PropertyValue -Object $candidate -Name "name")
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw "Candidate name is required."
        }

        if ($CandidateName.Count -gt 0 -and -not ($CandidateName | Where-Object { $_ -eq $name })) {
            continue
        }

        $enabled = [bool](Get-PropertyValue -Object $candidate -Name "enabled" -DefaultValue $true)
        if (-not $enabled -and -not $IncludeDisabledCandidates) {
            continue
        }

        $definitions += [pscustomobject]@{
            name = $name
            description = [string](Get-PropertyValue -Object $candidate -Name "description")
            disabledReason = [string](Get-PropertyValue -Object $candidate -Name "disabledReason")
            enabled = $enabled
            isBaseline = $false
            environment = Get-PropertyValue -Object $candidate -Name "environment"
        }
    }

    return $definitions
}

function Set-CalibrationEnvironment {
    param($EnvironmentMap)

    $previous = [ordered]@{}
    foreach ($variable in $AllowedCalibrationVariables) {
        $previous[$variable] = [Environment]::GetEnvironmentVariable($variable, "Process")
        [Environment]::SetEnvironmentVariable($variable, $null, "Process")
    }

    foreach ($entry in $EnvironmentMap.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process")
    }

    return $previous
}

function Restore-CalibrationEnvironment {
    param($PreviousEnvironment)

    foreach ($entry in $PreviousEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
    }
}

function Get-StatusScore {
    param([string]$Status)

    switch ($Status) {
        "pass" { return 5 }
        "not_asserted" { return 4 }
        "completed" { return 4 }
        "disabled" { return 3 }
        "dry_run" { return 2 }
        "skipped" { return 2 }
        "missing_data" { return 1 }
        "too_few" { return 0 }
        "too_many" { return 0 }
        "failed" { return 0 }
        default { return 1 }
    }
}

function Get-Number {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return 0
    }

    return [double]$Value
}

function Get-RunMetrics {
    param($Report)

    $results = @(ConvertTo-Array $Report.results)
    $falseAutoApplyCount = 0
    $missingExpectedNameCount = 0
    $unknownSpeakerCount = 0
    foreach ($result in $results) {
        $metrics = Get-PropertyValue -Object $result -Name "speakerNameMetrics"
        $falseAutoApplyCount += Get-Number (Get-PropertyValue -Object $metrics -Name "falseAutoApplyCount")
        $missingExpectedNameCount += Get-Number (Get-PropertyValue -Object $metrics -Name "missingExpectedNameCount")
        $unknownSpeakerCount += Get-Number (Get-PropertyValue -Object $metrics -Name "unknownSpeakerCount")
    }

    return [ordered]@{
        total = $results.Count
        completed = [int](Get-PropertyValue -Object $Report.summary -Name "completed" -DefaultValue 0)
        passed = [int](Get-PropertyValue -Object $Report.summary -Name "passed" -DefaultValue 0)
        tooFew = [int](Get-PropertyValue -Object $Report.summary -Name "tooFew" -DefaultValue 0)
        tooMany = [int](Get-PropertyValue -Object $Report.summary -Name "tooMany" -DefaultValue 0)
        missingData = [int](Get-PropertyValue -Object $Report.summary -Name "missingData" -DefaultValue 0)
        skipped = [int](Get-PropertyValue -Object $Report.summary -Name "skipped" -DefaultValue 0)
        failed = [int](Get-PropertyValue -Object $Report.summary -Name "failed" -DefaultValue 0)
        dryRun = [int](Get-PropertyValue -Object $Report.summary -Name "dryRun" -DefaultValue 0)
        falseAutoApplyCount = [int]$falseAutoApplyCount
        missingExpectedNameCount = [int]$missingExpectedNameCount
        unknownSpeakerCount = [int]$unknownSpeakerCount
    }
}

function Compare-CandidateReport {
    param(
        $BaselineReport,
        $CandidateReport
    )

    $baselineById = @{}
    foreach ($result in @(ConvertTo-Array $BaselineReport.results)) {
        $baselineById[[string]$result.fixtureId] = $result
    }

    $fixtureComparisons = @()
    foreach ($candidateResult in @(ConvertTo-Array $CandidateReport.results)) {
        $fixtureId = [string]$candidateResult.fixtureId
        if (-not $baselineById.ContainsKey($fixtureId)) {
            $fixtureComparisons += [ordered]@{
                fixtureId = $fixtureId
                category = [string]$candidateResult.category
                status = "missing_baseline"
            }
            continue
        }

        $baselineResult = $baselineById[$fixtureId]
        $baselineSpeakerScore = Get-StatusScore ([string]$baselineResult.speakerCountStatus)
        $candidateSpeakerScore = Get-StatusScore ([string]$candidateResult.speakerCountStatus)
        $baselineNameScore = Get-StatusScore ([string](Get-PropertyValue -Object $baselineResult.speakerNameMetrics -Name "status"))
        $candidateNameScore = Get-StatusScore ([string](Get-PropertyValue -Object $candidateResult.speakerNameMetrics -Name "status"))
        $status = if ($candidateSpeakerScore -gt $baselineSpeakerScore -or
            ($candidateSpeakerScore -eq $baselineSpeakerScore -and $candidateNameScore -gt $baselineNameScore)) {
            "improved"
        }
        elseif ($candidateSpeakerScore -lt $baselineSpeakerScore -or
            ($candidateSpeakerScore -eq $baselineSpeakerScore -and $candidateNameScore -lt $baselineNameScore)) {
            "regressed"
        }
        else {
            "unchanged"
        }

        $fixtureComparisons += [ordered]@{
            fixtureId = $fixtureId
            category = [string]$candidateResult.category
            status = $status
            baselineSpeakerCountStatus = [string]$baselineResult.speakerCountStatus
            candidateSpeakerCountStatus = [string]$candidateResult.speakerCountStatus
            baselineSpeakerNameStatus = [string](Get-PropertyValue -Object $baselineResult.speakerNameMetrics -Name "status")
            candidateSpeakerNameStatus = [string](Get-PropertyValue -Object $candidateResult.speakerNameMetrics -Name "status")
        }
    }

    $candidateMetrics = Get-RunMetrics -Report $CandidateReport
    $improved = @($fixtureComparisons | Where-Object { $_.status -eq "improved" })
    $regressed = @($fixtureComparisons | Where-Object { $_.status -eq "regressed" })
    $protectedRegressions = @($regressed | Where-Object { $ProtectedCategories -contains $_.category })
    $improvedCategories = @($improved | Select-Object -ExpandProperty category -Unique)
    $evidenceCount = @($fixtureComparisons | Where-Object {
            $_.status -ne "missing_baseline" -and
            $_.baselineSpeakerCountStatus -notin @("dry_run", "skipped", "missing_data") -and
            $_.candidateSpeakerCountStatus -notin @("dry_run", "skipped", "missing_data")
        }).Count

    $promotionStatus = if ($candidateMetrics.falseAutoApplyCount -gt 0) {
        "rejected_false_auto_apply"
    }
    elseif ($protectedRegressions.Count -gt 0) {
        "rejected_protected_regression"
    }
    elseif ($evidenceCount -eq 0) {
        "insufficient_evidence"
    }
    elseif ($improved.Count -ge 2 -or ($improved.Count -ge 1 -and $improvedCategories.Count -ge 2)) {
        "eligible_for_promotion"
    }
    else {
        "keep_current_defaults"
    }

    return [ordered]@{
        candidateName = [string]$CandidateReport.calibration.runName
        promotionStatus = $promotionStatus
        improvedFixtureCount = $improved.Count
        improvedCategories = $improvedCategories
        regressionCount = $regressed.Count
        protectedRegressionCount = $protectedRegressions.Count
        falseAutoApplyCount = $candidateMetrics.falseAutoApplyCount
        comparableEvidenceCount = $evidenceCount
        fixtureComparisons = $fixtureComparisons
    }
}

function Invoke-CatalogRun {
    param(
        $Candidate,
        [string]$ResolvedCatalogPath,
        [string]$ResolvedReportsRoot,
        [string]$CatalogScriptPath
    )

    $environmentMap = ConvertTo-EnvironmentMap -EnvironmentObject $Candidate.environment
    $safeName = Get-SafeFileNamePart -Value ([string]$Candidate.name)
    $runReportPath = Join-Path $ResolvedReportsRoot ("calibration-{0}-{1}.json" -f $safeName, (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss"))
    $parametersJson = $environmentMap | ConvertTo-Json -Depth 5 -Compress
    $arguments = @{
        CatalogPath = $ResolvedCatalogPath
        Configuration = $Configuration
        ReportsRoot = $ResolvedReportsRoot
        ReportPath = $runReportPath
        CalibrationRunName = [string]$Candidate.name
        CalibrationParametersJson = $parametersJson
    }
    if ($FixtureId.Count -gt 0) { $arguments.FixtureId = $FixtureId }
    if ($Category.Count -gt 0) { $arguments.Category = $Category }
    if ($IncludeDisabled) { $arguments.IncludeDisabled = $true }
    if ($DryRun) { $arguments.DryRun = $true }
    if ($NoBuild) { $arguments.NoBuild = $true }

    $previousEnvironment = Set-CalibrationEnvironment -EnvironmentMap $environmentMap
    try {
        $runStatus = "completed"
        $message = $null
        try {
            $null = & $CatalogScriptPath @arguments | Out-String
        }
        catch {
            $runStatus = "completed_with_fixture_failures"
            $message = $_.Exception.Message
            if (-not (Test-Path -LiteralPath $runReportPath -PathType Leaf)) {
                throw
            }
        }

        $report = Get-Content -LiteralPath $runReportPath -Raw | ConvertFrom-Json
        return [ordered]@{
            name = [string]$Candidate.name
            description = [string]$Candidate.description
            isBaseline = [bool]$Candidate.isBaseline
            status = $runStatus
            message = $message
            reportPath = $runReportPath
            parameters = $environmentMap
            metrics = Get-RunMetrics -Report $report
            report = $report
        }
    }
    finally {
        Restore-CalibrationEnvironment -PreviousEnvironment $previousEnvironment
    }
}

$script:RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).ProviderPath
if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    $CatalogPath = Join-Path $script:RepoRoot ".artifacts\diarization-fixtures\fixture-catalog.local.json"
}

$resolvedCatalogPath = Resolve-RequiredFile -PathValue $CatalogPath -Description "Fixture catalog"
if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
    $localCandidatePath = Join-Path $script:RepoRoot ".artifacts\diarization-fixtures\calibration-candidates.local.json"
    $CandidatePath = if (Test-Path -LiteralPath $localCandidatePath -PathType Leaf) { $localCandidatePath } else { $null }
}

$resolvedCandidatePath = if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
    $null
}
else {
    Resolve-RequiredFile -PathValue $CandidatePath -Description "Calibration candidate"
}

if ([string]::IsNullOrWhiteSpace($ReportsRoot)) {
    $ReportsRoot = Join-Path $script:RepoRoot ".artifacts\diarization-fixtures\reports"
}

$resolvedReportsRoot = Assert-AllowedOutputPath -PathValue $ReportsRoot -Description "ReportsRoot"
New-Item -ItemType Directory -Path $resolvedReportsRoot -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $resolvedReportsRoot ("diarization-calibration-comparison-{0}.json" -f (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss"))
}

$resolvedReportPath = Assert-AllowedOutputPath -PathValue $ReportPath -Description "ReportPath"
$catalogScriptPath = Resolve-RequiredFile -PathValue (Join-Path $script:RepoRoot "scripts\Test-DiarizationFixtureCatalog.ps1") -Description "Fixture catalog runner"
$candidates = @(Get-CandidateDefinitions -ResolvedCandidatePath $resolvedCandidatePath)
$runs = @()
foreach ($candidate in $candidates) {
    if (-not [bool]$candidate.enabled -and -not [bool]$candidate.isBaseline) {
        $runs += [ordered]@{
            name = [string]$candidate.name
            description = [string]$candidate.description
            isBaseline = $false
            status = "skipped"
            message = [string]$candidate.disabledReason
            reportPath = $null
            parameters = ConvertTo-EnvironmentMap -EnvironmentObject $candidate.environment
            metrics = $null
            report = $null
        }
        continue
    }

    Write-Output ("Running calibration candidate: {0}" -f $candidate.name)
    $runs += Invoke-CatalogRun -Candidate $candidate -ResolvedCatalogPath $resolvedCatalogPath -ResolvedReportsRoot $resolvedReportsRoot -CatalogScriptPath $catalogScriptPath
}

$baselineRun = $runs | Where-Object { $_.isBaseline } | Select-Object -First 1
if ($null -eq $baselineRun -or $null -eq $baselineRun.report) {
    throw "Baseline calibration run did not produce a report."
}

$comparisons = @()
foreach ($run in @($runs | Where-Object { -not $_.isBaseline -and $null -ne $_.report })) {
    $comparisons += Compare-CandidateReport -BaselineReport $baselineRun.report -CandidateReport $run.report
}

$eligibleCandidates = @($comparisons | Where-Object { $_.promotionStatus -eq "eligible_for_promotion" })
$recommendation = if ($eligibleCandidates.Count -eq 0) {
    [ordered]@{
        action = "keep_current_defaults"
        reason = "No candidate met the evidence and safety promotion rules."
        candidateName = $null
    }
}
else {
    $winner = $eligibleCandidates |
        Sort-Object -Property improvedFixtureCount, regressionCount -Descending |
        Select-Object -First 1
    [ordered]@{
        action = "candidate_eligible_for_manual_promotion"
        reason = "A candidate met the safety rules. Review the report before changing production defaults."
        candidateName = $winner.candidateName
    }
}

$finalReport = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    catalogPath = $resolvedCatalogPath
    candidatePath = $resolvedCandidatePath
    dryRun = [bool]$DryRun
    filters = [ordered]@{
        fixtureId = $FixtureId
        category = $Category
        includeDisabled = [bool]$IncludeDisabled
        candidateName = $CandidateName
        includeDisabledCandidates = [bool]$IncludeDisabledCandidates
    }
    protectedCategories = $ProtectedCategories
    allowedCalibrationVariables = $AllowedCalibrationVariables
    recommendation = $recommendation
    runs = @($runs | ForEach-Object {
            [ordered]@{
                name = $_.name
                description = $_.description
                isBaseline = $_.isBaseline
                status = $_.status
                message = $_.message
                reportPath = $_.reportPath
                parameters = $_.parameters
                metrics = $_.metrics
            }
        })
    comparisons = $comparisons
}

$finalReport | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $resolvedReportPath -Encoding UTF8

Write-Output ""
Write-Output "Diarization calibration summary:"
Write-Output "  Runs: $($runs.Count)"
Write-Output "  Comparisons: $($comparisons.Count)"
Write-Output "  Recommendation: $($recommendation.action)"
if ($recommendation.candidateName) {
    Write-Output "  Candidate: $($recommendation.candidateName)"
}
Write-Output "  Report: $resolvedReportPath"
