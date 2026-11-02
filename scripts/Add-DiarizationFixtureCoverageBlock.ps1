param(
    [Parameter(Mandatory = $true)]
    [string]$FixtureRoot,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$FixtureId,

    [Parameter(Mandatory = $true)]
    [ValidateSet('two-speaker', 'three-plus-speaker', 'short-call', 'noisy-similar-voices', 'overlap', 'hybrid-shared-mic', 'no-speech')]
    [string]$Category,

    [Parameter(Mandatory = $true)]
    [ValidateSet('no-approved-source', 'source-not-representative', 'consent-pending', 'hardware-not-available', 'native-inference-not-complete')]
    [string]$BlockedReason,

    [string]$ConsentRecord = 'coverage-gap-no-input',

    [string]$Classification = 'restricted-local-test',

    [string]$Owner = 'local-fixture-owner',

    [DateTime]$ExpiresAtUtc = (Get-Date).ToUniversalTime().AddYears(1)
)

$ErrorActionPreference = 'Stop'

function Test-PathUnder {
    param([string]$PathValue, [string]$RootValue)

    $path = [IO.Path]::GetFullPath($PathValue).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $root = [IO.Path]::GetFullPath($RootValue).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return $path.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
        $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Get-Sha256Hex {
    param([string]$Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($sha256.ComputeHash($bytes)).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

$resolvedFixtureRoot = (Resolve-Path -LiteralPath $FixtureRoot -ErrorAction Stop).ProviderPath
if (-not (Test-Path -LiteralPath $resolvedFixtureRoot -PathType Container)) {
    throw 'FixtureRoot must be an existing private directory.'
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$appDataRoot = Join-Path $env:LOCALAPPDATA 'MeetingRecorder'
if (Test-PathUnder -PathValue $resolvedFixtureRoot -RootValue $repoRoot) {
    throw 'FixtureRoot must stay outside source control.'
}
if (Test-PathUnder -PathValue $resolvedFixtureRoot -RootValue $appDataRoot) {
    throw 'FixtureRoot must stay outside Meeting Recorder app data.'
}

$catalogPath = Join-Path $resolvedFixtureRoot 'fixture-catalog.local.json'
$truthStorePath = Join-Path $resolvedFixtureRoot 'fixture-truth-store.local.json'
if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf) -or -not (Test-Path -LiteralPath $truthStorePath -PathType Leaf)) {
    throw 'FixtureRoot must contain an existing private catalog and truth store.'
}

$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$truthStore = Get-Content -LiteralPath $truthStorePath -Raw | ConvertFrom-Json
if ($catalog.schemaVersion -ne 2 -or $truthStore.schemaVersion -notin @(1, 2)) {
    throw 'Unsupported fixture catalog or truth-store schema.'
}
if (@($catalog.fixtures | Where-Object { $_.id -eq $FixtureId }).Count -gt 0) {
    throw 'Fixture ID already exists in the private catalog.'
}

# Disabled records deliberately have no source path or restricted expected outcome.
# The catalog runner skips them before resolving inputs or truth. Their deterministic
# hash identifies the block record itself, never a meeting asset.
$blockedOutcomeReference = "blocked-$FixtureId"
$blockRecordHash = Get-Sha256Hex -Value ("blocked|$FixtureId|$Category|$BlockedReason")
$fixture = [pscustomobject][ordered]@{
    id = $FixtureId
    version = 'blocked-v1'
    contentHash = $blockRecordHash
    category = $Category
    mode = 'full-audio'
    scenarioTags = @('coverage-gap', 'blocked', $Category)
    consentRecord = $ConsentRecord
    classification = $Classification
    owner = $Owner
    expiresAtUtc = $ExpiresAtUtc.ToUniversalTime().ToString('o')
    enabled = $false
    disabledReason = $BlockedReason
    expectedOutcomeRef = $blockedOutcomeReference
    inputRoot = $resolvedFixtureRoot
    manifestPath = ''
    audioPath = ''
    transcriptionSnapshotPath = ''
    transcriptJsonPath = ''
    configPath = ''
    enableSpeakerNameMatching = $false
    protectedArtifactPaths = @()
}

$catalog.fixtures = @($catalog.fixtures) + @($fixture)
$catalog | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $catalogPath -Encoding UTF8

Write-Output "Coverage block recorded. id=$FixtureId category=$Category reason=$BlockedReason"
