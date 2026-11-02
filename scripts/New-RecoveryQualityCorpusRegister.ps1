param(
    [Parameter(Mandatory)]
    [string]$FixtureCatalogPath,

    [Parameter(Mandatory)]
    [string]$RegistryPath,

    [string]$AggregateReportPath,

    [string[]]$RequiredClassification = @(
        "long-call",
        "short-call",
        "split-chain",
        "low-speech",
        "microphone-heavy",
        "endpoint-switch"),

    [ValidateRange(1, 10000)]
    [int]$MinimumEligibleCount = 25,

    [ValidateRange(1, 1440)]
    [int]$LongCallMinimumMinutes = 45
)

$ErrorActionPreference = "Stop"

function Resolve-RequiredFile {
    param([string]$PathValue, [string]$Description)

    $fullPath = [System.IO.Path]::GetFullPath($PathValue)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "$Description is unavailable."
    }

    return $fullPath
}

function Assert-PrivateRegisterPath {
    param([string]$PathValue)

    $fullPath = [System.IO.Path]::GetFullPath($PathValue)
    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
    $appDataRoot = Join-Path $env:LOCALAPPDATA "MeetingRecorder"
    foreach ($forbiddenRoot in @($repoRoot, $appDataRoot)) {
        $normalizedRoot = [System.IO.Path]::GetFullPath($forbiddenRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        if ($fullPath.Equals($normalizedRoot, [StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.StartsWith($normalizedRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Private corpus register cannot be stored in source control or Meeting Recorder app data."
        }
    }

    return $fullPath
}

function Assert-ReportPath {
    param([string]$PathValue)

    $fullPath = [System.IO.Path]::GetFullPath($PathValue)
    $repoArtifacts = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.artifacts")).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ((-not $fullPath.StartsWith($repoArtifacts + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) -and
        (-not $fullPath.StartsWith($tempRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
        throw "Aggregate report must be stored under .artifacts or the system temporary directory."
    }

    return $fullPath
}

function Test-Sha256 {
    param([string]$Value)
    return -not [string]::IsNullOrWhiteSpace($Value) -and $Value -match '^[A-Fa-f0-9]{64}$'
}

function Get-FileSha256 {
    param([string]$PathValue)

    $stream = [System.IO.File]::OpenRead($PathValue)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ($algorithm.ComputeHash($stream) | ForEach-Object { $_.ToString("x2") }) -join ""
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Test-PathUnder {
    param([string]$PathValue, [string]$RootValue)

    $path = [System.IO.Path]::GetFullPath($PathValue)
    $root = [System.IO.Path]::GetFullPath($RootValue).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    return $path.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
        $path.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Import-NAudioForDuration {
    if ($null -ne ('NAudio.Wave.AudioFileReader' -as [type])) {
        return $true
    }

    $packageRoot = Join-Path $env:USERPROFILE '.nuget\packages'
    $assemblyPaths = @(
        (Join-Path $packageRoot 'naudio.core\2.2.1\lib\netstandard2.0\NAudio.Core.dll'),
        (Join-Path $packageRoot 'naudio.winmm\2.2.1\lib\netstandard2.0\NAudio.WinMM.dll'),
        (Join-Path $packageRoot 'naudio.wasapi\2.2.1\lib\netstandard2.0\NAudio.Wasapi.dll'),
        (Join-Path $packageRoot 'naudio\2.2.1\lib\net472\NAudio.dll'))
    if (@($assemblyPaths | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count -gt 0) {
        return $false
    }

    foreach ($assemblyPath in $assemblyPaths) { Add-Type -Path $assemblyPath }
    return $null -ne ('NAudio.Wave.AudioFileReader' -as [type])
}

function Get-ApprovedAudioDurationMinutes {
    param($Fixture)

    $audioPath = [string]$Fixture.audioPath
    $inputRoot = [string]$Fixture.inputRoot
    if ([string]::IsNullOrWhiteSpace($audioPath) -or [string]::IsNullOrWhiteSpace($inputRoot)) {
        return $null
    }

    try {
        $resolvedAudioPath = [System.IO.Path]::GetFullPath($audioPath)
        if (-not (Test-Path -LiteralPath $resolvedAudioPath -PathType Leaf) -or -not (Test-PathUnder -PathValue $resolvedAudioPath -RootValue $inputRoot)) {
            return $null
        }
        if (-not (Import-NAudioForDuration)) {
            return $null
        }

        $reader = [NAudio.Wave.AudioFileReader]::new($resolvedAudioPath)
        try { return [double]$reader.TotalTime.TotalMinutes }
        finally { $reader.Dispose() }
    }
    catch {
        return $null
    }
}

function Write-AtomicJson {
    param($Value, [string]$DestinationPath)

    $directory = Split-Path -Path $DestinationPath -Parent
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $temporaryPath = "$DestinationPath.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $Value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
        Move-Item -LiteralPath $temporaryPath -Destination $DestinationPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

$resolvedCatalogPath = Resolve-RequiredFile -PathValue $FixtureCatalogPath -Description "Fixture catalog"
$resolvedRegistryPath = Assert-PrivateRegisterPath -PathValue $RegistryPath
if ([string]::IsNullOrWhiteSpace($AggregateReportPath)) {
    $AggregateReportPath = Join-Path $PSScriptRoot ("..\.artifacts\recovery-corpus\aggregate-{0}.json" -f (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss"))
}
$resolvedReportPath = Assert-ReportPath -PathValue $AggregateReportPath

$catalog = Get-Content -LiteralPath $resolvedCatalogPath -Raw | ConvertFrom-Json
if ([int]$catalog.schemaVersion -ne 2 -or $null -eq $catalog.fixtures) {
    throw "A governed fixture catalog schema version 2 is required."
}

$nowUtc = [DateTimeOffset]::UtcNow
$seenIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$clips = @()
foreach ($fixture in @($catalog.fixtures)) {
    $id = [string]$fixture.id
    $sourceHash = [string]$fixture.contentHash
    $classification = [string]$fixture.category
    $expiresAtUtc = [DateTimeOffset]::MinValue
    [void][DateTimeOffset]::TryParse([string]$fixture.expiresAtUtc, [ref]$expiresAtUtc)
    $enabled = [bool]$fixture.enabled
    $eligible = $enabled -and $seenIds.Add($id) -and (Test-Sha256 $sourceHash) -and
        -not [string]::IsNullOrWhiteSpace($classification) -and
        -not [string]::IsNullOrWhiteSpace([string]$fixture.consentRecord) -and
        -not [string]::IsNullOrWhiteSpace([string]$fixture.owner) -and $expiresAtUtc -gt $nowUtc
    $coverageClassifications = @($classification)
    if ($eligible) {
        $durationMinutes = Get-ApprovedAudioDurationMinutes -Fixture $fixture
        if ($null -ne $durationMinutes -and $durationMinutes -ge $LongCallMinimumMinutes) {
            $coverageClassifications += "long-call"
        }
    }
    $clips += [ordered]@{
        opaqueClipId = $id
        sourceSha256 = $sourceHash
        classification = $classification
        consentRecord = [string]$fixture.consentRecord
        owner = [string]$fixture.owner
        referenceRevision = [string]$fixture.version
        expiresAtUtc = $expiresAtUtc.ToUniversalTime().ToString("o")
        revokedAtUtc = $null
        eligible = $eligible
        coverageClassifications = @($coverageClassifications | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    }
}

$eligibleClips = @($clips | Where-Object { $_["eligible"] })
$eligibleByClassification = [ordered]@{}
foreach ($group in @($eligibleClips | ForEach-Object { @($_["coverageClassifications"]) } | Group-Object | Sort-Object Name)) {
    $eligibleByClassification[$group.Name] = $group.Count
}
$missingClassifications = @($RequiredClassification | Where-Object { -not $eligibleByClassification.Contains($_) } | Sort-Object -Unique)
$coverageState = if ($eligibleClips.Count -lt $MinimumEligibleCount) { "insufficient_clip_count" }
    elseif ($missingClassifications.Count -gt 0) { "missing_required_classifications" }
    else { "covered" }

$registry = [ordered]@{
    schemaVersion = 1
    createdAtUtc = $nowUtc.ToString("o")
    sourceCatalogSha256 = Get-FileSha256 -PathValue $resolvedCatalogPath
    clips = $clips
}
Write-AtomicJson -Value $registry -DestinationPath $resolvedRegistryPath

$report = [ordered]@{
    schemaVersion = 1
    createdAtUtc = $nowUtc.ToString("o")
    sourceCatalogSha256 = $registry.sourceCatalogSha256
    registrySha256 = Get-FileSha256 -PathValue $resolvedRegistryPath
    totalClipCount = $clips.Count
    eligibleClipCount = $eligibleClips.Count
    excludedClipCount = $clips.Count - $eligibleClips.Count
    minimumEligibleCount = $MinimumEligibleCount
    coverageState = $coverageState
    eligibleByClassification = $eligibleByClassification
    missingRequiredClassifications = $missingClassifications
}
Write-AtomicJson -Value $report -DestinationPath $resolvedReportPath

$report | ConvertTo-Json -Compress
