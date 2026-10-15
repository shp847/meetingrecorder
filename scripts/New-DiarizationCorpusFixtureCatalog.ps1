param(
    [Parameter(Mandatory = $true)]
    [string]$AudioRoot,

    [Parameter(Mandatory = $true)]
    [string]$TranscriptRoot,

    [Parameter(Mandatory = $true)]
    [string]$FixtureRoot,

    [string]$ConsentRecord = 'user-authorized-local-corpus',

    [string]$Classification = 'restricted-local-test',

    [string]$Owner = 'local-fixture-owner',

    [DateTime]$ExpiresAtUtc = (Get-Date).ToUniversalTime().AddYears(1),

    [switch]$NoCreateFixtures
)

$ErrorActionPreference = 'Stop'
$audioExtensions = @('.wav', '.m4a')
$transcriptExtensions = @('.md', '.vtt', '.txt')

function Resolve-Directory([string]$PathValue, [string]$Description) {
    $resolved = Resolve-Path -LiteralPath $PathValue -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.ProviderPath -PathType Container)) {
        throw "$Description must be a directory."
    }
    return $resolved.ProviderPath
}

function Get-Sha256([string]$PathValue) {
    $stream = [IO.File]::OpenRead($PathValue)
    try {
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try { return [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $sha256.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-OpaqueId([string]$Prefix, [string]$Hash) {
    return "$Prefix-$($Hash.Substring(0, 20))"
}

function Get-TextSha256([string]$Value) {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { return $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value)) }
    finally { $sha256.Dispose() }
}

function Normalize-Stem([string]$Value) {
    return (($Value.ToLowerInvariant() -replace '[^a-z0-9]+', '-') -replace '(^-+|-+$)', '')
}

function Get-DateKeys([string]$Value) {
    $numeric = @([regex]::Matches($Value, '(?<!\d)(20\d{2})[-_ /.]?(0[1-9]|1[0-2])[-_ /.]?([0-3]\d)(?!\d)') |
            ForEach-Object { '{0}-{1}-{2}' -f $_.Groups[1].Value, $_.Groups[2].Value, $_.Groups[3].Value } |
            Select-Object -Unique)
    $written = @([regex]::Matches($Value, '(?i)\b(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\.?\s+[0-3]?\d(?:st|nd|rd|th)?[,]?\s+20\d{2}\b') |
            ForEach-Object {
                $parsed = [DateTime]::MinValue
                $normalized = $_.Value -replace '(?i)(st|nd|rd|th)', ''
                if ([DateTime]::TryParse($normalized, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AllowWhiteSpaces, [ref]$parsed)) { $parsed.ToString('yyyy-MM-dd') }
            } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    return @($numeric + $written | Select-Object -Unique)
}

function Get-TranscriptFacts([string]$PathValue) {
    $lines = Get-Content -LiteralPath $PathValue -ErrorAction Stop
    $speakers = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $duration = [TimeSpan]::Zero
    $rangePattern = '^\s*\[(?<start>\d{1,2}:\d{2}:\d{2}(?:[\.,]\d{1,3})?)\s*-\s*(?<end>\d{1,2}:\d{2}:\d{2}(?:[\.,]\d{1,3})?)\]\s+\*\*(?<speaker>[^*:]{1,160}):\*\*\s*(?<text>.+?)\s*$'
    $pointPattern = '^\s*\*\*(?<speaker>[^*:\r\n]{1,160})\*\s*\*\[(?<start>\d{1,2}:\d{2}(?::\d{2})?(?:[\.,]\d{1,3})?)\]\*\s*:\s*(?<text>.+?)\s*$'
    foreach ($line in $lines) {
        if ([string]$line -match $rangePattern) {
            [void]$speakers.Add($Matches.speaker.Trim())
            $end = [TimeSpan]::Parse($Matches.end.Replace(',', '.'), [Globalization.CultureInfo]::InvariantCulture)
            if ($end -gt $duration) { $duration = $end }
            continue
        }
        if ([string]$line -match $pointPattern) {
            [void]$speakers.Add($Matches.speaker.Trim())
            $startValue = if ($Matches.start.Split(':').Count -eq 2) { "00:$($Matches.start)" } else { $Matches.start }
            $start = [TimeSpan]::Parse($startValue.Replace(',', '.'), [Globalization.CultureInfo]::InvariantCulture)
            if ($start -gt $duration) { $duration = $start }
        }
    }
    return [pscustomobject]@{ speakerCount = $speakers.Count; durationMilliseconds = [long]$duration.TotalMilliseconds }
}

function Import-NAudio {
    $packageRoot = Join-Path $env:USERPROFILE '.nuget\packages'
    $assemblyPaths = @(
        (Join-Path $packageRoot 'naudio.core\2.2.1\lib\netstandard2.0\NAudio.Core.dll'),
        (Join-Path $packageRoot 'naudio.winmm\2.2.1\lib\netstandard2.0\NAudio.WinMM.dll'),
        (Join-Path $packageRoot 'naudio.wasapi\2.2.1\lib\netstandard2.0\NAudio.Wasapi.dll'),
        (Join-Path $packageRoot 'naudio\2.2.1\lib\net472\NAudio.dll')
    )
    if (@($assemblyPaths | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count -gt 0) {
        throw 'NAudio 2.2.1 is required from the local NuGet cache to inspect private audio duration.'
    }
    if ($null -eq ('NAudio.Wave.AudioFileReader' -as [type])) {
        foreach ($assemblyPath in $assemblyPaths) { Add-Type -Path $assemblyPath }
    }
}

function Get-AudioDurationMilliseconds([string]$PathValue) {
    $reader = $null
    try {
        $reader = [NAudio.Wave.AudioFileReader]::new($PathValue)
        return [long]$reader.TotalTime.TotalMilliseconds
    }
    catch {
        return $null
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
    }
}

function Test-CompositeMatch($Audio, $Transcript) {
    if ($null -eq $Audio.durationMilliseconds -or $null -eq $Transcript.durationMilliseconds -or $Transcript.speakerCount -lt 1) {
        return $false
    }
    $sharedDate = @($Audio.dateKeys | Where-Object { $Transcript.dateKeys -contains $_ })
    if ($sharedDate.Count -eq 0) { return $false }
    $tolerance = [Math]::Max(120000, [Math]::Round([double]$Audio.durationMilliseconds * 0.10))
    return [Math]::Abs([long]$Audio.durationMilliseconds - [long]$Transcript.durationMilliseconds) -le $tolerance
}

function New-SourceRecord([IO.FileInfo]$File, [string]$Kind) {
    $hash = Get-Sha256 $File.FullName
    $baseName = $File.BaseName
    $dates = Get-DateKeys $baseName
    if ($Kind -eq 'transcript') {
        $head = (Get-Content -LiteralPath $File.FullName -TotalCount 80 -ErrorAction Stop) -join ' '
        $dates = @($dates + @(Get-DateKeys $head) | Select-Object -Unique)
        $facts = Get-TranscriptFacts $File.FullName
        return [pscustomobject]@{
            id = Get-OpaqueId 'transcript' $hash; kind = $Kind; hash = $hash; path = $File.FullName
            normalizedStem = Normalize-Stem $baseName; dateKeys = $dates; durationMilliseconds = $facts.durationMilliseconds
            speakerCount = $facts.speakerCount; extension = $File.Extension.ToLowerInvariant(); state = 'pending'
        }
    }
    return [pscustomobject]@{
        id = Get-OpaqueId 'audio' $hash; kind = $Kind; hash = $hash; path = $File.FullName
        normalizedStem = Normalize-Stem $baseName; dateKeys = $dates; durationMilliseconds = Get-AudioDurationMilliseconds $File.FullName
        speakerCount = $null; extension = $File.Extension.ToLowerInvariant(); state = 'pending'
    }
}

$resolvedAudioRoot = Resolve-Directory $AudioRoot 'Audio root'
$resolvedTranscriptRoot = Resolve-Directory $TranscriptRoot 'Transcript root'
$resolvedFixtureRoot = [IO.Path]::GetFullPath($FixtureRoot)
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
if ($resolvedFixtureRoot.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $resolvedFixtureRoot.StartsWith((Join-Path $env:LOCALAPPDATA 'MeetingRecorder'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'FixtureRoot must stay outside source control and Meeting Recorder app data.'
}
if (-not (Test-Path -LiteralPath $resolvedFixtureRoot)) { New-Item -ItemType Directory -Path $resolvedFixtureRoot -Force | Out-Null }
if ((Get-Item -LiteralPath $resolvedFixtureRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'FixtureRoot cannot be a reparse point.' }

Import-NAudio
$audio = @(Get-ChildItem -LiteralPath $resolvedAudioRoot -File -Recurse | Where-Object { $audioExtensions -contains $_.Extension.ToLowerInvariant() } | ForEach-Object { New-SourceRecord $_ 'audio' })
$transcripts = @(Get-ChildItem -LiteralPath $resolvedTranscriptRoot -File -Recurse | Where-Object { $transcriptExtensions -contains $_.Extension.ToLowerInvariant() } | ForEach-Object { New-SourceRecord $_ 'transcript' })

$paired = [System.Collections.Generic.List[object]]::new()
$audioByStem = @{}; foreach ($item in $audio) { if (-not $audioByStem.ContainsKey($item.normalizedStem)) { $audioByStem[$item.normalizedStem] = @() }; $audioByStem[$item.normalizedStem] += $item }
$transcriptsByStem = @{}; foreach ($item in $transcripts) { if (-not $transcriptsByStem.ContainsKey($item.normalizedStem)) { $transcriptsByStem[$item.normalizedStem] = @() }; $transcriptsByStem[$item.normalizedStem] += $item }
foreach ($stem in $audioByStem.Keys) {
    if ($transcriptsByStem.ContainsKey($stem) -and $audioByStem[$stem].Count -eq 1 -and $transcriptsByStem[$stem].Count -eq 1) {
        $a = $audioByStem[$stem][0]; $t = $transcriptsByStem[$stem][0]
        if ($t.speakerCount -gt 0 -and $t.extension -in @('.md', '.vtt')) { $paired.Add([pscustomobject]@{ audio = $a; transcript = $t; method = 'exact_stem' }); $a.state = 'matched'; $t.state = 'matched' }
    }
}
foreach ($a in @($audio | Where-Object state -eq 'pending')) {
    $candidates = @($transcripts | Where-Object { $_.state -eq 'pending' -and $_.extension -in @('.md', '.vtt') -and (Test-CompositeMatch $a $_) })
    if ($candidates.Count -ne 1) { continue }
    $t = $candidates[0]
    $reverse = @($audio | Where-Object { $_.state -eq 'pending' -and (Test-CompositeMatch $_ $t) })
    if ($reverse.Count -eq 1) { $paired.Add([pscustomobject]@{ audio = $a; transcript = $t; method = 'date_duration' }); $a.state = 'matched'; $t.state = 'matched' }
}
foreach ($a in $audio | Where-Object state -eq 'pending') {
    $a.state = if ($null -eq $a.durationMilliseconds) { 'held_unreadable_audio' } elseif (@($transcripts | Where-Object { Test-CompositeMatch $a $_ }).Count -gt 1) { 'held_ambiguous' } else { 'held_no_match' }
}
foreach ($t in $transcripts | Where-Object state -eq 'pending') {
    $t.state = if ($t.extension -notin @('.md', '.vtt') -or $t.speakerCount -lt 1) { 'held_unusable_transcript' } elseif (@($audio | Where-Object { Test-CompositeMatch $_ $t }).Count -gt 1) { 'held_ambiguous' } else { 'held_no_match' }
}

$catalogPath = Join-Path $resolvedFixtureRoot 'fixture-catalog.local.json'
$existingIds = @()
if (Test-Path -LiteralPath $catalogPath -PathType Leaf) { $existingIds = @((Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json).fixtures | ForEach-Object { $_.id }) }
$fixtureBuilder = Join-Path $PSScriptRoot 'New-DiarizationVttFixture.ps1'
$buildResults = [System.Collections.Generic.List[object]]::new()
foreach ($pair in $paired) {
    $pairHash = Get-TextSha256 "$($pair.audio.hash):$($pair.transcript.hash)"
    $fixtureId = 'corpus-' + ([BitConverter]::ToString($pairHash).Replace('-', '').ToLowerInvariant().Substring(0, 20))
    if ($existingIds -contains $fixtureId) { $buildResults.Add([pscustomobject]@{ fixtureId = $fixtureId; state = 'existing' }); continue }
    if ($NoCreateFixtures) { $buildResults.Add([pscustomobject]@{ fixtureId = $fixtureId; state = 'planned' }); continue }
    try {
        $arguments = @{ AudioPath = $pair.audio.path; FixtureRoot = $resolvedFixtureRoot; FixtureId = $fixtureId; ConsentRecord = $ConsentRecord; Classification = $Classification; Owner = $Owner; ExpiresAtUtc = $ExpiresAtUtc }
        if (Test-Path -LiteralPath $catalogPath -PathType Leaf) { $arguments.Append = $true }
        if ($pair.transcript.extension -eq '.vtt') { $arguments.VttPath = $pair.transcript.path } else { $arguments.TranscriptMarkdownPath = $pair.transcript.path }
        $null = & $fixtureBuilder @arguments | Out-String
        $existingIds += $fixtureId
        $buildResults.Add([pscustomobject]@{ fixtureId = $fixtureId; state = 'created' })
    }
    catch { $buildResults.Add([pscustomobject]@{ fixtureId = $fixtureId; state = 'held_build_failed' }) }
}

$sourceHashesUnchanged = @($audio + $transcripts | ForEach-Object { (Get-Sha256 $_.path) -eq $_.hash } | Where-Object { -not $_ }).Count -eq 0
$report = [ordered]@{
    schemaVersion = 1; generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o'); sourceHashesUnchanged = $sourceHashesUnchanged
    summary = [ordered]@{ audio = $audio.Count; transcripts = $transcripts.Count; matched = $paired.Count; exactStem = @($paired | Where-Object method -eq 'exact_stem').Count; dateDuration = @($paired | Where-Object method -eq 'date_duration').Count; held = @($audio + $transcripts | Where-Object { $_.state -ne 'matched' }).Count; created = @($buildResults | Where-Object state -eq 'created').Count; existing = @($buildResults | Where-Object state -eq 'existing').Count; buildHeld = @($buildResults | Where-Object state -eq 'held_build_failed').Count }
    inputs = @($audio + $transcripts | ForEach-Object { [ordered]@{ id = $_.id; kind = $_.kind; state = $_.state } })
    pairs = @($paired | ForEach-Object { [ordered]@{ audioId = $_.audio.id; transcriptId = $_.transcript.id; method = $_.method } })
    fixtures = @($buildResults)
}
$reportPath = Join-Path $resolvedFixtureRoot 'corpus-intake-report.local.json'
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Output "Corpus intake complete. Audio=$($audio.Count) Transcripts=$($transcripts.Count) Matched=$($paired.Count) Created=$(@($buildResults | Where-Object state -eq 'created').Count) Held=$(@($audio + $transcripts | Where-Object { $_.state -ne 'matched' }).Count)"
