param(
    [Parameter(Mandatory = $true)]
    [string]$JsonPath,

    [int]$ExpectedSpeakerCount = 0,

    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

function Resolve-RequiredPath {
    param(
        [string]$PathValue,
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "$Description path is required."
    }

    $resolved = Resolve-Path -LiteralPath $PathValue -ErrorAction Stop
    return $resolved.ProviderPath
}

function Get-SegmentSpeakerLabel {
    param($Segment)

    if ($Segment.PSObject.Properties["speakerLabel"] -and -not [string]::IsNullOrWhiteSpace([string]$Segment.speakerLabel)) {
        return [string]$Segment.speakerLabel
    }

    if ($Segment.PSObject.Properties["speakerId"] -and -not [string]::IsNullOrWhiteSpace([string]$Segment.speakerId)) {
        return [string]$Segment.speakerId
    }

    return "(unlabeled)"
}

if ($ExpectedSpeakerCount -ne 0 -and ($ExpectedSpeakerCount -lt 2 -or $ExpectedSpeakerCount -gt 16)) {
    throw "ExpectedSpeakerCount must be 2-16 when supplied."
}

$resolvedJsonPath = Resolve-RequiredPath -PathValue $JsonPath -Description "Transcript JSON"
$document = Get-Content -LiteralPath $resolvedJsonPath -Raw | ConvertFrom-Json
$segments = @($document.segments)
if ($segments.Count -eq 0) {
    throw "Transcript JSON did not contain a non-empty segments array."
}

$labelCounts = @{}
$speakerRunCount = 0
$lastLabel = $null
foreach ($segment in $segments) {
    $label = Get-SegmentSpeakerLabel -Segment $segment
    if ($labelCounts.ContainsKey($label)) {
        $labelCounts[$label] = [int]$labelCounts[$label] + 1
    }
    else {
        $labelCounts[$label] = 1
    }

    if ($null -eq $lastLabel -or $lastLabel -ne $label) {
        $speakerRunCount++
        $lastLabel = $label
    }
}

$speakerLabelCount = @($labelCounts.Keys | Where-Object { $_ -ne "(unlabeled)" }).Count
$status = "no expected speaker count supplied"
if ($ExpectedSpeakerCount -gt 0) {
    if ($speakerLabelCount -eq $ExpectedSpeakerCount) {
        $status = "matches expected speaker count"
    }
    elseif ($speakerLabelCount -gt $ExpectedSpeakerCount) {
        $status = "over-segmented"
    }
    else {
        $status = "collapsed below expected speaker count"
    }
}

$labelDistribution = @(
    $labelCounts.GetEnumerator() |
        Sort-Object -Property Value -Descending |
        ForEach-Object {
            [pscustomobject]@{
                Label = $_.Key
                SegmentCount = $_.Value
            }
        })

$result = [pscustomobject]@{
    TranscriptJsonPath = $resolvedJsonPath
    SegmentCount = $segments.Count
    SpeakerLabelCount = $speakerLabelCount
    SpeakerRunCount = $speakerRunCount
    ExpectedSpeakerCount = if ($ExpectedSpeakerCount -eq 0) { $null } else { $ExpectedSpeakerCount }
    Status = $status
    LabelDistribution = $labelDistribution
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 6
    return
}

Write-Output "Diarization calibration:"
Write-Output "  Transcript JSON: $($result.TranscriptJsonPath)"
Write-Output "  Segments: $($result.SegmentCount)"
Write-Output "  Speaker labels: $($result.SpeakerLabelCount)"
Write-Output "  Speaker runs: $($result.SpeakerRunCount)"
if ($result.ExpectedSpeakerCount) {
    Write-Output "  Expected speakers: $($result.ExpectedSpeakerCount)"
}
Write-Output "  Status: $($result.Status)"
Write-Output "  Label distribution:"
foreach ($entry in $result.LabelDistribution) {
    Write-Output "    $($entry.Label): $($entry.SegmentCount) segment(s)"
}
