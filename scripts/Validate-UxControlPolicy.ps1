[CmdletBinding()]
param(
    [switch]$RefreshPolicy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$auditRoot = Join-Path $repositoryRoot 'docs\ux-audits'
$inventoryPath = Join-Path $auditRoot 'whole-app-sprint-0-controls.json'
$schemaPath = Join-Path $auditRoot 'whole-app-sprint-1-policy.schema.json'
$policyPath = Join-Path $auditRoot 'whole-app-sprint-1-policy.json'
$requirementsPath = Join-Path $repositoryRoot 'PRODUCT_REQUIREMENTS.md'
$planPath = Join-Path $repositoryRoot 'plan.md'

$layers = @('assistant', 'guided', 'power')
$aliasBehaviors = @('primary', 'navigate', 'contextual-duplicate')
$automationDispositions = @(
    'automatic',
    'automatic-after-persisted-opt-in',
    'recommend-only',
    'explicit-per-action'
)
$consentKinds = @('not-required', 'persisted-opt-in', 'explicit-per-action')

function Assert-FileExists {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required policy file is missing: $Path"
    }
}

function Get-Text {
    param([Parameter(Mandatory)]$Control)
    return "$($Control.id) $($Control.visibleCopy) $($Control.commandOrHandler)"
}

function Get-RiskCategories {
    param([Parameter(Mandatory)]$Control)

    $text = Get-Text -Control $Control
    $risks = [System.Collections.Generic.List[string]]::new()
    if ($text -match '(?i)mic|microphone') { $risks.Add('microphone') }
    if ($text -match '(?i)openai|modelproxy|hosted|summary' -and $text -notmatch '(?i)clear.*key') { $risks.Add('hosted-transcript-transfer') }
    if ($text -match '(?i)delete|archive|cleanup.*apply|apply.*cleanup|clear.*key' -and $text -notmatch '(?i)incrementalsafecleanup') { $risks.Add('destructive') }
    if ($text -match '(?i)incrementalsafecleanup') { $risks.Add('safe-cleanup') }
    if ($text -match '(?i)retranscribe|process.*asap|queue|worker|install.*update|update.*install') { $risks.Add('active-work-interruption') }
    return @($risks | Select-Object -Unique)
}

function Get-AutomationDisposition {
    param([Parameter(Mandatory)]$Control)

    $id = [string]$Control.id
    $text = Get-Text -Control $Control
    if ($id -match 'config(miccapture|autodetect|summarygeneration|speakernamelearning|updatecheckenabled|autoinstallupdates|incrementalqueuedrecordings|incrementalspeakerlabels|incrementalaisummaries|incrementalsafecleanup)' -or
        $id -match 'configbackgroundprocessingmode|configbackgroundspeakerlabelingmode') {
        return 'automatic-after-persisted-opt-in'
    }
    if ($text -match '(?i)review.*suggestion|recommendation.*datagrid|next best action') {
        return 'recommend-only'
    }
    return 'explicit-per-action'
}

function Get-PersistedOptIn {
    param(
        [Parameter(Mandatory)]$Control,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Risks
    )

    $text = Get-Text -Control $Control
    if ($Risks -contains 'microphone') {
        return [pscustomobject][ordered]@{
            kind = 'persisted-opt-in'
            scope = 'Microphone contribution for future local recordings.'
            dataBoundary = 'Microphone audio remains in local meeting artifacts unless the user separately enables a hosted summary.'
            reversalPath = 'Turn off microphone capture in Settings or Home before the next recording.'
            reconfirmWhen = 'Capture scope changes or a new microphone source is selected.'
        }
    }
    if ($Risks -contains 'hosted-transcript-transfer') {
        return [pscustomobject][ordered]@{
            kind = 'persisted-opt-in'
            scope = 'Future meeting summaries with the currently selected provider.'
            dataBoundary = 'Transcript text leaves this device only for the explicitly selected hosted provider.'
            reversalPath = 'Disable summaries or select Local-only in Settings before later processing.'
            reconfirmWhen = 'Hosted provider, fallback provider, or transcript data scope changes.'
        }
    }
    if ($text -match '(?i)update') {
        return [pscustomobject][ordered]@{
            kind = 'persisted-opt-in'
            scope = 'The named update check or idle install capability only.'
            dataBoundary = 'Release metadata is checked or downloaded; no meeting content is transferred.'
            reversalPath = 'Turn off the named update setting in Settings.'
            reconfirmWhen = 'Update channel or install behavior changes.'
        }
    }
    if ($text -match '(?i)speaker.*learn') {
        return [pscustomobject][ordered]@{
            kind = 'persisted-opt-in'
            scope = 'Local speaker-name learning from user-confirmed corrections.'
            dataBoundary = 'Voice-profile embeddings remain local to this user profile.'
            reversalPath = 'Turn off learning or delete local profiles in Settings.'
            reconfirmWhen = 'Learning scope or profile storage behavior changes.'
        }
    }
    if ($text -match '(?i)cleanup') {
        return [pscustomobject][ordered]@{
            kind = 'persisted-opt-in'
            scope = 'The named safe cleanup capability for eligible local meeting artifacts.'
            dataBoundary = 'Only local artifacts that meet the displayed eligibility rule are considered.'
            reversalPath = 'Turn off safe cleanup before the next background pass; restore from archive when available.'
            reconfirmWhen = 'Eligible artifact classes or cleanup scope changes.'
        }
    }
    return [pscustomobject][ordered]@{
        kind = 'persisted-opt-in'
        scope = 'The named background capability only.'
        dataBoundary = 'The policy row identifies the local data boundary for this capability.'
        reversalPath = 'Turn off the named setting in Settings.'
        reconfirmWhen = 'Capability scope changes.'
    }
}

function Get-ExplicitConsent {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Risks,
        [Parameter(Mandatory)][string]$ControlLabel
    )

    $scope = if ($Risks -contains 'destructive') {
        'Displayed eligible meeting artifacts only.'
    }
    elseif ($Risks -contains 'hosted-transcript-transfer') {
        'The selected transcript and the currently selected summary provider.'
    }
    elseif ($Risks -contains 'microphone') {
        'Microphone contribution for the selected recording scope.'
    }
    elseif ($Risks -contains 'active-work-interruption') {
        'The selected queued, active, or update work item.'
    }
    else {
        "The action '$ControlLabel'."
    }

    $boundary = if ($Risks -contains 'hosted-transcript-transfer') {
        'Transcript text leaves this device only after the user approves the named hosted provider.'
    }
    elseif ($Risks -contains 'destructive') {
        'Affected local artifacts, irreversible impact, and archive availability are shown before execution.'
    }
    elseif ($Risks -contains 'active-work-interruption') {
        'Affected active work, cancellation behavior, and recovery route are shown before execution.'
    }
    elseif ($Risks -contains 'microphone') {
        'Microphone audio capture is shown before it is enabled.'
    }
    else {
        'No hidden data transfer or background mutation is authorized by this action.'
    }

    return [pscustomobject][ordered]@{
        kind = 'explicit-per-action'
        scope = $scope
        dataBoundary = $boundary
        reversalPath = 'Cancel before execution or use the displayed recovery route after execution.'
        reconfirmWhen = if ($Risks -contains 'hosted-transcript-transfer') { 'Every execution, and whenever hosted provider, fallback provider, or transcript scope changes.' } else { 'Every execution, and again if affected scope changes.' }
    }
}

function Get-PolicyRows {
    param([Parameter(Mandatory)]$Inventory)

    $controls = @($Inventory.controls)
    $byId = @{}
    foreach ($control in $controls) {
        $byId[[string]$control.id] = $control
    }

    $primaryByControlId = @{}
    foreach ($control in $controls) {
        $group = @($control.id) + @($control.duplicateAliases)
        $primaryByControlId[[string]$control.id] = @($group | Sort-Object | Select-Object -First 1)[0]
    }

    $rows = foreach ($control in $controls | Sort-Object id) {
        $id = [string]$control.id
        $primaryId = [string]$primaryByControlId[$id]
        $primary = $byId[$primaryId]
        $isPrimary = $id -eq $primaryId
        $aliasBehavior = if ($isPrimary) {
            'primary'
        }
        elseif ($control.surface -eq $primary.surface) {
            'contextual-duplicate'
        }
        else {
            'navigate'
        }
        $risks = @(Get-RiskCategories -Control $control)
        $automationDisposition = Get-AutomationDisposition -Control $control
        $label = if ([string]::IsNullOrWhiteSpace([string]$control.visibleCopy)) { $id } else { [string]$control.visibleCopy }
        $consent = if ($automationDisposition -eq 'automatic-after-persisted-opt-in') {
            Get-PersistedOptIn -Control $control -Risks $risks
        }
        elseif ($risks.Count -gt 0 -or $automationDisposition -eq 'explicit-per-action') {
            Get-ExplicitConsent -Risks $risks -ControlLabel $label
        }
        else {
            [pscustomobject][ordered]@{
                kind = 'not-required'
                scope = 'Local metadata-only display or recommendation.'
                dataBoundary = 'No content leaves this device and no artifact is mutated.'
                reversalPath = 'No change is made.'
                reconfirmWhen = 'Not applicable.'
            }
        }
        $defaultBehavior = switch ($automationDisposition) {
            'automatic-after-persisted-opt-in' { 'Off until user saves opt-in; then only named capability runs within stated scope.'; break }
            'recommend-only' { 'Shows a local, bounded recommendation; execution remains a separate explicit action.'; break }
            default { 'No background mutation; this control performs a direct user-requested action or navigation.' }
        }

        [pscustomobject][ordered]@{
            controlId = $id
            primaryOwnerControlId = $primaryId
            primaryOwnerSurface = [string]$primary.surface
            layer = [string]$control.targetLayer
            aliasBehavior = $aliasBehavior
            aliasTargetControlId = $primaryId
            defaultBehavior = $defaultBehavior
            userVisibleStatusLocation = "Current viewport: $($control.surface) status and next-action region."
            futureOwnerSprint = [string]$control.futureOwnerSprint
            automationDisposition = $automationDisposition
            riskCategories = $risks
            consent = $consent
        }
    }

    return @($rows)
}

function Get-PolicyError {
    param(
        [Parameter(Mandatory)]$Row,
        [Parameter(Mandatory)]$RowsById
    )

    if ($Row.aliasBehavior -notin $aliasBehaviors) { return 'invalid alias behavior' }
    if ($Row.automationDisposition -notin $automationDispositions) { return 'invalid automation disposition' }
    if ($Row.layer -notin $layers) { return 'invalid layer' }
    if ($Row.consent.kind -notin $consentKinds) { return 'invalid consent kind' }
    if (-not $RowsById.ContainsKey([string]$Row.primaryOwnerControlId)) { return 'missing primary owner' }
    if (-not $RowsById.ContainsKey([string]$Row.aliasTargetControlId)) { return 'invalid alias target' }

    $target = $RowsById[[string]$Row.aliasTargetControlId]
    if ($target.primaryOwnerControlId -ne $target.controlId) { return 'alias target is not primary' }
    if ($Row.aliasBehavior -eq 'primary' -and $Row.controlId -ne $Row.primaryOwnerControlId) { return 'primary row points elsewhere' }
    if ($Row.aliasBehavior -ne 'primary' -and $Row.controlId -eq $Row.primaryOwnerControlId) { return 'alias row points to itself' }
    if ($Row.primaryOwnerSurface -ne $target.primaryOwnerSurface) { return 'owner surface does not match alias target' }
    if ($Row.userVisibleStatusLocation -notmatch '^Current viewport:') { return 'missing current-viewport status location' }
    if ([string]::IsNullOrWhiteSpace([string]$Row.futureOwnerSprint)) { return 'missing future owner sprint' }

    $isHighRisk = @($Row.riskCategories).Count -gt 0
    if ($isHighRisk -and $Row.automationDisposition -in @('automatic', 'recommend-only')) { return 'high-risk action cannot be automatic or recommendation-only' }
    if ($isHighRisk -and $Row.consent.kind -eq 'not-required') { return 'high-risk action lacks consent rule' }
    if ($Row.automationDisposition -eq 'automatic-after-persisted-opt-in') {
        foreach ($field in @('scope', 'dataBoundary', 'reversalPath', 'reconfirmWhen')) {
            if ([string]::IsNullOrWhiteSpace([string]$Row.consent.$field)) { return "persisted opt-in missing $field" }
        }
    }
    if ($Row.riskCategories -contains 'hosted-transcript-transfer') {
        if ($Row.consent.dataBoundary -notmatch 'leaves this device' -or $Row.consent.reconfirmWhen -notmatch 'provider') { return 'hosted boundary is incomplete' }
    }
    if ($Row.riskCategories -contains 'destructive' -and $Row.automationDisposition -ne 'explicit-per-action') { return 'destructive action must be explicit-per-action' }
    if ($Row.riskCategories -contains 'active-work-interruption' -and $Row.controlId -match '(?i)install.*update|update.*install' -and $Row.automationDisposition -ne 'explicit-per-action' -and $Row.controlId -notmatch 'configautoinstallupdates') { return 'update installation must be explicit-per-action' }
    return $null
}

function Write-Policy {
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
    $rows = Get-PolicyRows -Inventory $inventory
    $policy = [ordered]@{
        schemaVersion = '1.0'
        inventorySchemaVersion = [string]$inventory.schemaVersion
        scope = 'Whole-App UX Simplification And Control Balance / Sprint 1'
        automationMatrix = @(
            [ordered]@{ disposition = 'automatic'; allowedWhen = 'Local, bounded, non-overlapping metadata refresh or recommendation generation with visible current-viewport status.'; forbidden = 'Hosted transfer, microphone, destructive work, or active-work interruption.' },
            [ordered]@{ disposition = 'automatic-after-persisted-opt-in'; allowedWhen = 'User saved named capability scope, data boundary, reversal path, and re-confirmation trigger.'; forbidden = 'Permission for another provider, microphone path, destructive action, or unrelated capability.' },
            [ordered]@{ disposition = 'recommend-only'; allowedWhen = 'Local eligibility/ranking is shown with no mutation.'; forbidden = 'Executing, archiving, deleting, reprocessing, or transferring content.' },
            [ordered]@{ disposition = 'explicit-per-action'; allowedWhen = 'User reviews visible scope and confirms the individual action.'; forbidden = 'Silent execution or broadened alias behavior.' }
        )
        validationFixtures = @(
            [ordered]@{ id = 'automatic-local-refresh'; automationDisposition = 'automatic'; riskCategories = @(); expectedValid = $true },
            [ordered]@{ id = 'persisted-microphone-opt-in'; automationDisposition = 'automatic-after-persisted-opt-in'; riskCategories = @('microphone'); expectedValid = $true },
            [ordered]@{ id = 'recommendation-only'; automationDisposition = 'recommend-only'; riskCategories = @(); expectedValid = $true },
            [ordered]@{ id = 'explicit-permanent-delete'; automationDisposition = 'explicit-per-action'; riskCategories = @('destructive'); expectedValid = $true },
            [ordered]@{ id = 'alias-conflict'; automationDisposition = 'explicit-per-action'; riskCategories = @(); aliasTargetControlId = 'missing-primary'; expectedValid = $false }
        )
        controls = $rows
    }
    $policy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $policyPath -Encoding utf8
    Write-Host "Refreshed $($rows.Count) policy rows."
}

Assert-FileExists -Path $inventoryPath
Assert-FileExists -Path $schemaPath
Assert-FileExists -Path $requirementsPath
Assert-FileExists -Path $planPath

if ($RefreshPolicy) {
    Write-Policy
}

Assert-FileExists -Path $policyPath
$inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
$schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
if ($schema.properties.schemaVersion.const -ne '1.0' -or $policy.schemaVersion -ne '1.0') { throw 'Policy schemaVersion must be 1.0.' }
if ($policy.inventorySchemaVersion -ne $inventory.schemaVersion) { throw 'Policy references a different inventory schema version.' }

$inventoryIds = @($inventory.controls | ForEach-Object { [string]$_.id } | Sort-Object)
$rows = @($policy.controls)
$rowIds = @($rows | ForEach-Object { [string]$_.controlId } | Sort-Object)
if (($rowIds | Select-Object -Unique).Count -ne $rowIds.Count) { throw 'Policy contains duplicate control ownership rows.' }
if (@(Compare-Object -ReferenceObject $inventoryIds -DifferenceObject $rowIds).Count -gt 0) { throw 'Policy does not provide exactly one row for every Sprint 0 control id.' }

$rowsById = @{}
foreach ($row in $rows) { $rowsById[[string]$row.controlId] = $row }
foreach ($row in $rows) {
    $validationError = Get-PolicyError -Row $row -RowsById $rowsById
    if ($null -ne $validationError) { throw "Policy row '$($row.controlId)' failed: $validationError." }
    $heading = "^## $([regex]::Escape([string]$row.futureOwnerSprint))[:]"
    if (-not (Select-String -LiteralPath $planPath -Pattern $heading -Quiet)) { throw "Policy row '$($row.controlId)' points to missing sprint '$($row.futureOwnerSprint)'." }
}

$fixtureDispositions = @($policy.validationFixtures | ForEach-Object automationDisposition | Sort-Object -Unique)
if (@(Compare-Object -ReferenceObject @($automationDispositions | Sort-Object) -DifferenceObject $fixtureDispositions).Count -gt 0) { throw 'Validation fixtures must cover all four automation dispositions.' }
$aliasConflict = @($policy.validationFixtures | Where-Object { $_.id -eq 'alias-conflict' } | Select-Object -First 1)
if ($aliasConflict.Count -ne 1 -or $aliasConflict[0].expectedValid -ne $false -or $aliasConflict[0].aliasTargetControlId -ne 'missing-primary') { throw 'Alias-conflict fixture is missing or does not prove invalid target handling.' }

$requirements = Get-Content -LiteralPath $requirementsPath -Raw
foreach ($heading in @('Three layers', 'Automation and consent matrix', 'One primary owner', 'Approved copy patterns')) {
    if ($requirements -notmatch [regex]::Escape($heading)) { throw "PRODUCT_REQUIREMENTS.md is missing policy heading '$heading'." }
}

Write-Host "UX control policy valid: $($rows.Count) ownership rows, $($policy.validationFixtures.Count) fixtures, and all high-risk actions consent-gated."
