[CmdletBinding()]
param(
    [switch]$RefreshInventory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$auditRoot = Join-Path $repositoryRoot 'docs\ux-audits'
$schemaPath = Join-Path $auditRoot 'whole-app-sprint-0-controls.schema.json'
$inventoryPath = Join-Path $auditRoot 'whole-app-sprint-0-controls.json'
$journeysPath = Join-Path $auditRoot 'whole-app-sprint-0-journeys.md'
$frictionPath = Join-Path $auditRoot 'whole-app-sprint-0-friction.md'
$renderedEvidencePath = Join-Path $auditRoot 'whole-app-sprint-0-rendered-evidence.md'
$dispositionPath = Join-Path $auditRoot 'whole-app-sprint-0-disposition.md'
$planPath = Join-Path $repositoryRoot 'plan.md'

$auditSources = @(
    'src/MeetingRecorder.App/MainWindow.xaml',
    'src/MeetingRecorder.App/MeetingDetailWindow.xaml',
    'src/MeetingRecorder.App/SetupWindow.xaml'
)

$interactiveTags = @(
    'Button', 'MenuItem', 'Hyperlink', 'TextBox', 'PasswordBox', 'ComboBox',
    'CheckBox', 'RadioButton', 'TabItem', 'Expander', 'ToggleButton', 'Slider',
    'ListBox', 'DataGrid', 'KeyBinding', 'MouseBinding'
)

function Get-AttributeValue {
    param(
        [Parameter(Mandatory)][string]$Attributes,
        [Parameter(Mandatory)][string[]]$Names
    )

    foreach ($name in $Names) {
        $pattern = '(?:^|\s)' + [regex]::Escape($name) + '="(?<value>[^"]*)"'
        $match = [regex]::Match($Attributes, $pattern)
        if ($match.Success) {
            return $match.Groups['value'].Value
        }
    }

    return $null
}

function Get-SourceAnchorLine {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][int]$Index
    )

    return 1 + ([regex]::Matches($Text.Substring(0, $Index), "`n")).Count
}

function Get-ControlSurface {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][int]$Index,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Name
    )

    if ($Source.EndsWith('MeetingDetailWindow.xaml', [StringComparison]::Ordinal)) {
        return 'Meeting detail'
    }

    if ($Source.EndsWith('SetupWindow.xaml', [StringComparison]::Ordinal)) {
        return 'Setup dialog'
    }

    if ($Name -match '^Header') {
        return 'Header'
    }

    $dashboardStart = $Text.IndexOf('<TabItem x:Name="DashboardTabItem"', [StringComparison]::Ordinal)
    $meetingsStart = $Text.IndexOf('<TabItem x:Name="MeetingsTabItem"', [StringComparison]::Ordinal)
    $settingsStart = $Text.IndexOf('<Border x:Name="SettingsSetupTranscriptionBodyHostBorder"', [StringComparison]::Ordinal)
    if ($dashboardStart -lt 0 -or $meetingsStart -lt 0 -or $settingsStart -lt 0) {
        throw "MainWindow section markers are missing; cannot classify '$Name'."
    }

    if ($Index -ge $settingsStart) { return 'Settings' }
    if ($Index -ge $meetingsStart) { return 'Meetings' }
    if ($Index -ge $dashboardStart) { return 'Home' }
    return 'Home'
}

function Get-ControlClassification {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$NameAndCopy)

    if ($NameAndCopy -match '(?i)delete|remove|archive|clear') { return 'destructive action' }
    if ($NameAndCopy -match '(?i)microphone|mic capture|speaker name|voice profile|attendee') { return 'privacy decision' }
    if ($NameAndCopy -match '(?i)openai|modelproxy|hosted|summary') { return 'privacy/cost decision' }
    if ($NameAndCopy -match '(?i)update|install|retranscribe|process asap|queue') { return 'processing interruption' }
    if ($NameAndCopy -match '(?i)threshold|timeout|\bcli\b|provider|gpu|feed url|arguments|overnight') { return 'advanced tuning' }
    if ($NameAndCopy -match '(?i)test|probe|validate|refresh|diagnostic|copy.*path') { return 'diagnostic-only' }
    if ($NameAndCopy -match '(?i)settings|setup|help|open.*folder|open.*audio|open.*transcript') { return 'recovery action' }
    return 'safe default'
}

function Get-TargetLayer {
    param(
        [Parameter(Mandatory)][string]$Surface,
        [Parameter(Mandatory)][string]$Classification
    )

    if ($Classification -in @('advanced tuning', 'diagnostic-only')) { return 'power' }
    if ($Surface -eq 'Settings' -and $Classification -eq 'safe default') { return 'guided' }
    if ($Classification -eq 'safe default' -and $Surface -in @('Home', 'Meetings')) { return 'assistant' }
    return 'guided'
}

function Get-OwnerSprint {
    param(
        [Parameter(Mandatory)][string]$Surface,
        [Parameter(Mandatory)][string]$Classification,
        [Parameter(Mandatory)][string]$NameAndCopy
    )

    if ($NameAndCopy -match '(?i)speaker|voice profile') { return 'Sprint 13' }
    if ($NameAndCopy -match '(?i)summary|openai|modelproxy') { return 'Sprint 12' }
    if ($Surface -eq 'Meeting detail') { return 'Sprint 9' }
    if ($Surface -eq 'Setup dialog') { return 'Sprint 4' }
    if ($Surface -eq 'Settings') {
        if ($Classification -in @('advanced tuning', 'diagnostic-only')) { return 'Sprint 3' }
        return 'Sprint 2'
    }
    if ($Surface -eq 'Meetings') {
        if ($Classification -eq 'destructive action') { return 'Sprint 8' }
        if ($NameAndCopy -match '(?i)cleanup|recommend') { return 'Sprint 7' }
        return 'Sprint 6'
    }
    if ($Surface -eq 'Header') { return 'Sprint 14' }
    return 'Sprint 5'
}

function Get-Disposition {
    param(
        [Parameter(Mandatory)][string]$Classification,
        [Parameter(Mandatory)][bool]$IsDuplicate,
        [Parameter(Mandatory)][string]$Surface
    )

    if ($Classification -in @('destructive action', 'privacy decision', 'privacy/cost decision', 'processing interruption')) { return 'retain' }
    if ($Classification -in @('advanced tuning', 'diagnostic-only')) { return 'move-to-advanced' }
    if ($IsDuplicate) { return 'combine' }
    if ($Surface -eq 'Settings') { return 'combine' }
    return 'retain'
}

function Get-InteractiveControls {
    $records = [System.Collections.Generic.List[object]]::new()
    $tagAlternation = ($interactiveTags | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $tagPattern = "(?s)<(?<tag>$tagAlternation)\b(?<attributes>[^>]*)>"

    foreach ($source in $auditSources) {
        $path = Join-Path $repositoryRoot $source
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Audit source is missing: $source"
        }

        $text = Get-Content -LiteralPath $path -Raw
        $ordinal = 0
        foreach ($match in [regex]::Matches($text, $tagPattern)) {
            $ordinal++
            $attributes = $match.Groups['attributes'].Value
            $tag = $match.Groups['tag'].Value
            $line = Get-SourceAnchorLine -Text $text -Index $match.Index
            $name = Get-AttributeValue -Attributes $attributes -Names @('x:Name', 'Name')
            $copy = Get-AttributeValue -Attributes $attributes -Names @('Content', 'Header', 'Text', 'ToolTip')
            $handler = Get-AttributeValue -Attributes $attributes -Names @('Click', 'Command')
            $visibility = Get-AttributeValue -Attributes $attributes -Names @('Visibility', 'IsEnabled')
            $effectiveName = if ($null -eq $name) { '' } else { $name }
            $effectiveCopy = if ($null -eq $copy) { '' } else { $copy }
            $effectiveHandler = if ($null -eq $handler) { '' } else { $handler }
            $sourceStem = [IO.Path]::GetFileNameWithoutExtension($source) -replace '[^A-Za-z0-9]+', '-'
            $stableId = if ([string]::IsNullOrWhiteSpace($name)) {
                "$sourceStem-$tag-$ordinal".ToLowerInvariant()
            }
            else {
                "control-$name".ToLowerInvariant()
            }
            $surface = Get-ControlSurface -Source $source -Text $text -Index $match.Index -Name $effectiveName
            $nameAndCopy = "$name $copy $handler"
            $classification = Get-ControlClassification -NameAndCopy $nameAndCopy
            $records.Add([pscustomobject][ordered]@{
                    id = $stableId
                    surface = $surface
                    visibleCopy = $effectiveCopy
                    controlType = $tag
                    sourceAnchor = "$($source.Replace([char]92, [char]47)):$line"
                    commandOrHandler = $effectiveHandler
                    visibilityOrEnabledCondition = if ([string]::IsNullOrWhiteSpace($visibility)) { 'Visible by XAML default; runtime gating is documented in source handler or bindings.' } else { $visibility }
                    actionClassification = $classification
                    duplicateAliases = @()
                    currentUserPurpose = if ([string]::IsNullOrWhiteSpace($copy)) { "Use $tag control $effectiveName$ordinal." } else { "Use '$copy'." }
                    targetLayer = ''
                    targetSurface = ''
                    futureOwnerSprint = ''
                    safetyRationale = ''
                    disposition = ''
                    replacementContract = ''
                })
        }
    }

    foreach ($idGroup in @($records | Group-Object id | Where-Object Count -gt 1)) {
        $sequence = 0
        foreach ($record in @($idGroup.Group | Sort-Object sourceAnchor)) {
            $sequence++
            $record.id = "$($record.id)-$sequence"
        }
    }

    $handlerGroups = $records | Where-Object { -not [string]::IsNullOrWhiteSpace($_.commandOrHandler) } | Group-Object commandOrHandler
    foreach ($record in $records) {
        $duplicates = @($handlerGroups | Where-Object Name -eq $record.commandOrHandler | ForEach-Object { $_.Group | Where-Object id -ne $record.id | ForEach-Object id })
        $record.duplicateAliases = $duplicates
        $record.targetLayer = Get-TargetLayer -Surface $record.surface -Classification $record.actionClassification
        $record.targetSurface = if ($record.targetLayer -eq 'power') { 'Settings > Advanced' } else { $record.surface }
        $record.futureOwnerSprint = Get-OwnerSprint -Surface $record.surface -Classification $record.actionClassification -NameAndCopy "$($record.id) $($record.visibleCopy) $($record.commandOrHandler)"
        $record.disposition = Get-Disposition -Classification $record.actionClassification -IsDuplicate ($duplicates.Count -gt 0) -Surface $record.surface
        $record.safetyRationale = switch ($record.actionClassification) {
            'destructive action' { 'Keep explicit confirmation and result evidence; do not automate or hide this action.'; break }
            'privacy decision' { 'Keep a direct, informed choice because this can alter capture or identity data.'; break }
            'privacy/cost decision' { 'Keep a direct, informed choice because this can send content off-device or incur cost.'; break }
            'processing interruption' { 'Keep a direct choice because this can interrupt, reorder, or restart user work.'; break }
            'advanced tuning' { 'Preserve expert control, but isolate it from the normal guided workflow.'; break }
            'diagnostic-only' { 'Keep support access without making diagnostics a daily decision.'; break }
            default { 'May be consolidated only when the current capability and recovery route remain available.' }
        }
    }

    return @($records | Sort-Object sourceAnchor, id)
}

function Assert-FileExists {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Required audit file is missing: $Path" }
}

function Assert-NoPrivatePayload {
    param([Parameter(Mandatory)][string[]]$Paths)

    $privatePayloadPattern = '(?i)(?:[A-Z]:\\Users\\|\\\\Users\\|/Users/|sk-[A-Za-z0-9_-]{8,}|-----BEGIN(?: [A-Z]+)? PRIVATE KEY-----)'
    foreach ($path in $Paths) {
        $text = Get-Content -LiteralPath $path -Raw
        if ($text -match $privatePayloadPattern) {
            throw "Private path, key, or transcript-like payload detected in audit evidence: $path"
        }
    }
}

function Write-Inventory {
    New-Item -ItemType Directory -Path $auditRoot -Force | Out-Null
    $controls = Get-InteractiveControls
    $inventory = [ordered]@{
        schemaVersion = '1.0'
        scope = 'Whole-App UX Simplification And Control Balance / Sprint 0'
        sources = $auditSources
        controls = $controls
    }
    $inventory | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $inventoryPath -Encoding utf8

    $rows = foreach ($control in $controls) {
        $aliases = if ($control.duplicateAliases.Count -eq 0) { 'none' } else { ($control.duplicateAliases -join ', ') }
        "| $($control.id) | $($control.disposition) | $($control.targetLayer) | $($control.targetSurface) | $($control.futureOwnerSprint) | $aliases |"
    }
    @(
        '# Whole-App Sprint 0 Control Disposition',
        '',
        'Generated from the committed inventory by `scripts/Validate-UxAudit.ps1 -RefreshInventory`.',
        'No control is retired in Sprint 0. Later owner sprints must retain every explicit consent,',
        'destructive, privacy, cost, microphone, and processing-interruption boundary named here.',
        '',
        '| Control id | Disposition | Layer | Target surface | Owner sprint | Duplicate aliases |',
        '| --- | --- | --- | --- | --- | --- |'
    ) + $rows | Set-Content -LiteralPath $dispositionPath -Encoding utf8

    Write-Host "Refreshed $($controls.Count) controls and disposition map."
}

if ($RefreshInventory) {
    Write-Inventory
}

foreach ($path in @($schemaPath, $inventoryPath, $journeysPath, $frictionPath, $renderedEvidencePath, $dispositionPath, $planPath)) {
    Assert-FileExists -Path $path
}

$schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
if ($schema.properties.schemaVersion.const -ne '1.0') { throw 'Audit schema must lock schemaVersion to 1.0.' }

$inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
if ($inventory.schemaVersion -ne '1.0') { throw 'Inventory schemaVersion must be 1.0.' }

$controls = @($inventory.controls)
if ($controls.Count -eq 0) { throw 'Control inventory is empty.' }
$ids = @($controls | ForEach-Object id)
if (($ids | Select-Object -Unique).Count -ne $ids.Count) { throw 'Control inventory contains duplicate ids.' }

$expectedControls = Get-InteractiveControls
$expectedAnchors = @($expectedControls | ForEach-Object sourceAnchor | Sort-Object)
$actualAnchors = @($controls | ForEach-Object sourceAnchor | Sort-Object)
if (@(Compare-Object -ReferenceObject $expectedAnchors -DifferenceObject $actualAnchors).Count -gt 0) {
    throw 'Control-to-source coverage is stale. Run Validate-UxAudit.ps1 -RefreshInventory and review dispositions.'
}

$requiredProperties = @('id', 'surface', 'visibleCopy', 'controlType', 'sourceAnchor', 'commandOrHandler', 'visibilityOrEnabledCondition', 'actionClassification', 'duplicateAliases', 'currentUserPurpose', 'targetLayer', 'targetSurface', 'futureOwnerSprint', 'safetyRationale', 'disposition', 'replacementContract')
$allowedDispositions = @('retain', 'combine', 'automate', 'move-to-advanced', 'move-contextually', 'retire')
foreach ($control in $controls) {
    foreach ($property in $requiredProperties) {
        if ($null -eq $control.PSObject.Properties[$property]) { throw "Control '$($control.id)' is missing '$property'." }
    }
    if ($control.disposition -notin $allowedDispositions) { throw "Control '$($control.id)' has invalid disposition '$($control.disposition)'." }
    if ($control.disposition -eq 'retire' -and [string]::IsNullOrWhiteSpace($control.replacementContract)) { throw "Retired control '$($control.id)' requires a replacement contract." }
    $sprintPattern = '^Sprint (?:[0-9]+|10A)$'
    if ($control.futureOwnerSprint -notmatch $sprintPattern) { throw "Control '$($control.id)' has invalid owner sprint '$($control.futureOwnerSprint)'." }
    $heading = "^## $([regex]::Escape($control.futureOwnerSprint))[:]"
    if (-not (Select-String -LiteralPath $planPath -Pattern $heading -Quiet)) { throw "Control '$($control.id)' points to missing plan heading '$($control.futureOwnerSprint)'." }
}

$journeys = Get-Content -LiteralPath $journeysPath -Raw
foreach ($journeyId in 1..9 | ForEach-Object { 'J{0:D2}' -f $_ }) {
    if ($journeys -notmatch "(?m)^## $journeyId\s") { throw "Journey $journeyId is missing." }
}

$renderedEvidence = Get-Content -LiteralPath $renderedEvidencePath -Raw
foreach ($state in @('empty/healthy', 'setup-blocked', 'processing', 'selection-active', 'cleanup-recommendation')) {
    if ($renderedEvidence -notmatch [regex]::Escape($state)) { throw "Rendered-evidence state '$state' is missing." }
}

$friction = Get-Content -LiteralPath $frictionPath -Raw
$scoreRows = [regex]::Matches($friction, '(?m)^[|] [SM][0-9]+ .*?[|] [1-5] [|] [1-5] [|] [1-3] [|] [1-9][0-9]? [|]')
if ($scoreRows.Count -lt 6) { throw 'Friction audit needs at least three scored Settings findings and three scored Meetings findings.' }

Assert-NoPrivatePayload -Paths @($inventoryPath, $journeysPath, $frictionPath, $renderedEvidencePath, $dispositionPath)

Write-Host "UX audit valid: $($controls.Count) controls, nine journeys, five rendered states, and $($scoreRows.Count) scored findings."
