param(
    [Parameter(Mandatory = $true)]
    [string]$AudioPath,

    [string]$VttPath,

    [string]$TranscriptMarkdownPath,

    [Parameter(Mandatory = $true)]
    [string]$FixtureRoot,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$FixtureId,

    [switch]$Append,

    [ValidateSet('auto', 'one-speaker', 'two-speaker', 'three-plus-speaker', 'short-call', 'noisy-similar-voices', 'overlap', 'hybrid-shared-mic', 'no-speech')]
    [string]$Category = 'auto',

    [string]$ConsentRecord = 'user-authorized-local-fixture',

    [string]$Classification = 'restricted-local-test',

    [string]$Owner = 'local-fixture-owner',

    [DateTime]$ExpiresAtUtc = (Get-Date).ToUniversalTime().AddYears(1),

    [string]$ModelCacheDir = (Join-Path $env:LOCALAPPDATA 'MeetingRecorder\models'),

    [string]$DiarizationAssetPath = (Join-Path $env:LOCALAPPDATA 'MeetingRecorder\models\diarization')
)

$ErrorActionPreference = 'Stop'

function Resolve-RequiredFile {
    param([string]$PathValue, [string]$Description)

    $resolved = Resolve-Path -LiteralPath $PathValue -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.ProviderPath -PathType Leaf)) {
        throw "$Description path is not a file."
    }

    return $resolved.ProviderPath
}

function ConvertTo-VttTimeSpan {
    param([string]$Value)

    $normalized = $Value.Trim().Replace(',', '.')
    $parsed = [TimeSpan]::Zero
    if (-not [TimeSpan]::TryParse($normalized, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        throw 'Transcript timestamp is invalid.'
    }

    return $parsed
}

function Get-FileSha256 {
    param([string]$PathValue)

    $stream = [IO.File]::OpenRead($PathValue)
    try {
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            return [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '')
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Read-VttSegments {
    param([string]$PathValue)

    $lines = Get-Content -LiteralPath $PathValue
    $segments = [System.Collections.Generic.List[object]]::new()
    $speakerTokens = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $index = 0
    while ($index -lt $lines.Count) {
        $line = [string]$lines[$index]
        if ($line -notmatch '^\s*(?<start>\d{1,2}:\d{2}:\d{2}(?:[\.,]\d{1,3})?)\s*-->\s*(?<end>\d{1,2}:\d{2}:\d{2}(?:[\.,]\d{1,3})?)') {
            $index++
            continue
        }

        $start = ConvertTo-VttTimeSpan -Value $Matches.start
        $end = ConvertTo-VttTimeSpan -Value $Matches.end
        $index++
        $content = [System.Collections.Generic.List[string]]::new()
        while ($index -lt $lines.Count -and -not [string]::IsNullOrWhiteSpace([string]$lines[$index])) {
            $content.Add([string]$lines[$index])
            $index++
        }

        $text = ($content -join ' ').Trim()
        if ([string]::IsNullOrWhiteSpace($text)) {
            continue
        }

        $speaker = $null
        if ($text -match '^(?<speaker>[^:]{1,160}):\s*(?<text>.*)$') {
            $speaker = $Matches.speaker.Trim()
            $text = $Matches.text.Trim()
        }

        if (-not [string]::IsNullOrWhiteSpace($speaker)) {
            [void]$speakerTokens.Add($speaker)
        }

        if (-not [string]::IsNullOrWhiteSpace($text) -and $end -gt $start) {
            $segments.Add([ordered]@{
                    start = $start.ToString('c', [Globalization.CultureInfo]::InvariantCulture)
                    end = $end.ToString('c', [Globalization.CultureInfo]::InvariantCulture)
                    speakerId = $null
                    speakerLabel = $null
                    text = $text
                })
        }
    }

    if ($segments.Count -eq 0) {
        throw 'Transcript contains no usable VTT segments.'
    }
    if ($speakerTokens.Count -lt 1 -or $speakerTokens.Count -gt 16) {
        throw 'Transcript speaker count must be from 1 through 16.'
    }

    return [pscustomobject]@{
        Segments = $segments
        SpeakerCount = $speakerTokens.Count
    }
}

function Read-MarkdownSegments {
    param([string]$PathValue)

    $lines = Get-Content -LiteralPath $PathValue
    $segments = [System.Collections.Generic.List[object]]::new()
    $speakerTokens = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $linePattern = '^\s*\[(?<start>\d{1,2}:\d{2}:\d{2}(?:[\.,]\d{1,3})?)\s*-\s*(?<end>\d{1,2}:\d{2}:\d{2}(?:[\.,]\d{1,3})?)\]\s+\*\*(?<speaker>[^*:]{1,160}):\*\*\s*(?<text>.+?)\s*$'
    foreach ($line in $lines) {
        if ([string]$line -notmatch $linePattern) {
            continue
        }

        $start = ConvertTo-VttTimeSpan -Value $Matches.start
        $end = ConvertTo-VttTimeSpan -Value $Matches.end
        $speaker = $Matches.speaker.Trim()
        $text = $Matches.text.Trim()
        if ([string]::IsNullOrWhiteSpace($speaker) -or [string]::IsNullOrWhiteSpace($text) -or $end -le $start) {
            continue
        }

        [void]$speakerTokens.Add($speaker)
        $segments.Add([ordered]@{
                start = $start.ToString('c', [Globalization.CultureInfo]::InvariantCulture)
                end = $end.ToString('c', [Globalization.CultureInfo]::InvariantCulture)
                speakerId = $null
                speakerLabel = $null
                text = $text
            })
    }

    if ($segments.Count -eq 0) {
        throw 'Transcript Markdown contains no usable timestamped speaker segments.'
    }
    if ($speakerTokens.Count -lt 1 -or $speakerTokens.Count -gt 16) {
        throw 'Transcript Markdown speaker count must be from 1 through 16.'
    }

    return [pscustomobject]@{
        Segments = $segments
        SpeakerCount = $speakerTokens.Count
    }
}

$resolvedAudioPath = Resolve-RequiredFile -PathValue $AudioPath -Description 'Audio'
if ([string]::IsNullOrWhiteSpace($VttPath) -eq [string]::IsNullOrWhiteSpace($TranscriptMarkdownPath)) {
    throw 'Pass exactly one of VttPath or TranscriptMarkdownPath.'
}

$parsed = if (-not [string]::IsNullOrWhiteSpace($VttPath)) {
    Read-VttSegments -PathValue (Resolve-RequiredFile -PathValue $VttPath -Description 'VTT transcript')
}
else {
    Read-MarkdownSegments -PathValue (Resolve-RequiredFile -PathValue $TranscriptMarkdownPath -Description 'Transcript Markdown')
}
$resolvedFixtureRoot = [System.IO.Path]::GetFullPath($FixtureRoot)
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
if ($resolvedFixtureRoot.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'FixtureRoot must stay outside source control.'
}

$fixtureDirectory = Join-Path $resolvedFixtureRoot $FixtureId
if (Test-Path -LiteralPath $fixtureDirectory) {
    throw 'Fixture directory already exists. Choose a new fixture ID.'
}

$catalogPath = Join-Path $resolvedFixtureRoot 'fixture-catalog.local.json'
$truthStorePath = Join-Path $resolvedFixtureRoot 'fixture-truth-store.local.json'
$catalogExists = Test-Path -LiteralPath $catalogPath -PathType Leaf
$truthStoreExists = Test-Path -LiteralPath $truthStorePath -PathType Leaf
if ($catalogExists -ne $truthStoreExists) {
    throw 'Fixture catalog and truth store must either both exist or both be absent.'
}
if (($catalogExists -or $truthStoreExists) -and -not $Append) {
    throw 'Fixture catalog already exists. Pass -Append only after reviewing the existing private catalog and truth store.'
}
if ($Append -and -not $catalogExists) {
    throw 'Append requires an existing private fixture catalog and truth store.'
}

$inferredCategory = if ($parsed.SpeakerCount -eq 1) {
    'one-speaker'
}
elseif ($parsed.SpeakerCount -eq 2) {
    'two-speaker'
}
else {
    'three-plus-speaker'
}
if ($Category -eq 'auto') {
    $Category = $inferredCategory
}
elseif ($Category -in @('one-speaker', 'two-speaker', 'three-plus-speaker') -and $Category -ne $inferredCategory) {
    throw "Category does not match VTT speaker-count taxonomy; use '$inferredCategory' or a non-count scenario category."
}

$audioDestination = Join-Path $fixtureDirectory ('source' + [IO.Path]::GetExtension($resolvedAudioPath).ToLowerInvariant())
$manifestPath = Join-Path $fixtureDirectory 'manifest.json'
$processingRoot = Join-Path $fixtureDirectory 'processing'
$snapshotPath = Join-Path $processingRoot 'transcription.snapshot.json'
$configPath = Join-Path $fixtureDirectory 'fixture-config.json'

New-Item -ItemType Directory -Path $processingRoot -Force | Out-Null
Copy-Item -LiteralPath $resolvedAudioPath -Destination $audioDestination -ErrorAction Stop

$now = (Get-Date).ToUniversalTime()
$snapshot = [ordered]@{
    segments = @($parsed.Segments)
    language = 'en'
    message = 'Private VTT fixture snapshot.'
}
$manifest = [ordered]@{
    sessionId = $FixtureId
    platform = 0
    detectedTitle = $FixtureId
    startedAtUtc = $now.ToString('o')
    state = 1
    mergedAudioPath = $audioDestination
    processingOverrides = [ordered]@{ skipSpeakerLabeling = $false }
}
$config = [ordered]@{
    audioOutputDir = (Join-Path $fixtureDirectory 'probe-audio')
    transcriptOutputDir = (Join-Path $fixtureDirectory 'probe-transcripts')
    workDir = (Join-Path $fixtureDirectory 'probe-work')
    modelCacheDir = $ModelCacheDir
    diarizationAssetPath = $DiarizationAssetPath
    transcriptionProviderPreference = 0
    diarizationProviderPreference = 0
    summaryGenerationMode = 0
    speakerNameLearningMode = 0
    diarizationAccelerationPreference = 1
}

$snapshot | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $snapshotPath -Encoding UTF8
$manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
$config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $configPath -Encoding UTF8

$contentHash = Get-FileSha256 -PathValue $audioDestination
$truthReference = "truth-$FixtureId"
$fixture = [ordered]@{
            id = $FixtureId
            version = '1'
            contentHash = $contentHash
            category = $Category
            mode = 'full-audio'
            scenarioTags = @($Category, 'vtt-derived', 'private')
            consentRecord = $ConsentRecord
            classification = $Classification
            owner = $Owner
            expiresAtUtc = $ExpiresAtUtc.ToUniversalTime().ToString('o')
            enabled = $true
            expectedOutcomeRef = $truthReference
            inputRoot = $fixtureDirectory
            manifestPath = $manifestPath
            audioPath = $audioDestination
            transcriptionSnapshotPath = $snapshotPath
            configPath = $configPath
            enableSpeakerNameMatching = $false
            protectedArtifactPaths = @($audioDestination, $manifestPath, $snapshotPath, $configPath)
}
$outcome = [ordered]@{
            reference = $truthReference
            expectedSpeakerCount = $parsed.SpeakerCount
            expectedSpeakerNames = @()
}

if ($Append) {
    $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    $truthStore = Get-Content -LiteralPath $truthStorePath -Raw | ConvertFrom-Json
    if ($catalog.schemaVersion -ne 2 -or $truthStore.schemaVersion -ne 1) {
        throw 'Unsupported fixture catalog or truth-store schema.'
    }
    if (@($catalog.fixtures | Where-Object { $_.id -eq $FixtureId }).Count -gt 0) {
        throw 'Fixture ID already exists in the private catalog.'
    }
    if (@($truthStore.outcomes | Where-Object { $_.reference -eq $truthReference }).Count -gt 0) {
        throw 'Fixture truth reference already exists in the private truth store.'
    }
    $catalog.fixtures = @($catalog.fixtures) + @([pscustomobject]$fixture)
    $truthStore.outcomes = @($truthStore.outcomes) + @([pscustomobject]$outcome)
}
else {
    $catalog = [ordered]@{
        schemaVersion = 2
        fixtures = @($fixture)
    }
    $truthStore = [ordered]@{
        schemaVersion = 1
        outcomes = @($outcome)
    }
}

$catalog | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $catalogPath -Encoding UTF8
$truthStore | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $truthStorePath -Encoding UTF8

Write-Output "Private fixture created. id=$FixtureId category=$Category expectedSpeakerCount=$($parsed.SpeakerCount)"
