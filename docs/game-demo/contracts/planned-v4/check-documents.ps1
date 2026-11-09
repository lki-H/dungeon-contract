[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$taskCache = 'D:/dev-cache/dungeon-contract/tmp/technical-audit-21/schema'
$null = New-Item -ItemType Directory -Path $taskCache -Force
$taskBattle = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'battle.schema.json') -Raw | ConvertFrom-Json -AsHashtable -Depth 100
$taskApi = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'openapi.json') -Raw | ConvertFrom-Json -AsHashtable -Depth 100
$taskPositive = 0
$taskNegative = 0
function Test-DocumentCase {
    param([string]$Name, [object]$Value, [object]$Schema, [bool]$Expected)
    $documentPath = Join-Path $taskCache ($Name + '.json')
    $schemaPath = Join-Path $taskCache ($Name + '.schema.json')
    [IO.File]::WriteAllText($documentPath, (ConvertTo-Json -InputObject $Value -Depth 100))
    [IO.File]::WriteAllText($schemaPath, (ConvertTo-Json -InputObject $Schema -Depth 100))
    $actual = Test-Json -LiteralPath $documentPath -SchemaFile $schemaPath -ErrorAction SilentlyContinue
    if ($actual -ne $Expected) { throw "Schema assertion failed: $Name expected=$Expected actual=$actual" }
    if ($Expected) { $script:taskPositive++ } else { $script:taskNegative++ }
}
function Read-Document {
    param([string]$Name)
    Get-Content -LiteralPath (Join-Path $PSScriptRoot $Name) -Raw | ConvertFrom-Json -AsHashtable -Depth 100
}
function Copy-Document {
    param([object]$Value)
    ConvertTo-Json -InputObject $Value -Depth 100 | ConvertFrom-Json -AsHashtable -Depth 100
}
$taskMappings = @{
    'parameters.json' = 'parameters.schema.json'
    'terminal-frame.example.json' = 'frame.schema.json'
}
foreach ($entry in $taskMappings.GetEnumerator()) {
    Test-DocumentCase -Name $entry.Key -Value (Read-Document $entry.Key) -Schema (Read-Document $entry.Value) -Expected $true
}
$taskBattleExamples = @('input', 'switch', 'spawn', 'baseline', 'dynamic', 'presentation', 'action-receipt', 'action-rejected', 'state-update')
foreach ($name in $taskBattleExamples) {
    Test-DocumentCase -Name $name -Value (Read-Document ($name + '.example.json')) -Schema $taskBattle -Expected $true
}
$taskControlExamples = @{
    'pause' = 'PauseOrResumeControl'
    'resume' = 'PauseOrResumeControl'
    'control-ack' = 'ControlAck'
    'snapshot' = 'OwnRunSnapshot'
    'control-receipt' = 'ControlReceipt'
}
foreach ($entry in $taskControlExamples.GetEnumerator()) {
    $schema = @{ '$schema' = 'https://json-schema.org/draft/2020-12/schema' } + $taskApi.components.schemas[$entry.Value]
    Test-DocumentCase -Name $entry.Key -Value (Read-Document ($entry.Key + '.example.json')) -Schema $schema -Expected $true
}
foreach ($chunk in (Read-Document 'baseline-chunks.example.json')) {
    Test-DocumentCase -Name ('baseline-chunk-' + $chunk.chunk_index) -Value $chunk -Schema $taskBattle -Expected $true
}
$taskBadInput = Copy-Document (Read-Document 'input.example.json')
foreach ($entry in @{ protocol_version = 3; weapon_revision = 0; connection_generation = 0; aim_u16 = 65536; action_watermark = -1; damage = 999 }.GetEnumerator()) {
    $changed = Copy-Document $taskBadInput
    $changed[$entry.Key] = $entry.Value
    Test-DocumentCase -Name ('bad-input-' + $entry.Key) -Value $changed -Schema $taskBattle -Expected $false
}
$taskSwitch = Read-Document 'switch.example.json'
foreach ($entry in @{ requested_weapon_id = $null; switch_id = $null; action_sequence = 0; action = 'grant_reward'; press_action_id = 'wrong_press' }.GetEnumerator()) {
    $changed = Copy-Document $taskSwitch
    $changed[$entry.Key] = $entry.Value
    Test-DocumentCase -Name ('bad-switch-' + $entry.Key) -Value $changed -Schema $taskBattle -Expected $false
}
$taskRelease = Copy-Document $taskSwitch
$taskRelease.action = 'attack_release'
$taskRelease.requested_weapon_id = $null
$taskRelease.switch_id = $null
$taskRelease.press_action_id = 'press_example'
Test-DocumentCase -Name 'release' -Value $taskRelease -Schema $taskBattle -Expected $true
$taskRelease.press_action_id = $null
Test-DocumentCase -Name 'release-no-press' -Value $taskRelease -Schema $taskBattle -Expected $false
$taskChoice = Copy-Document $taskSwitch
$taskChoice.action = 'choose_growth'
$taskChoice.requested_weapon_id = $null
$taskChoice.switch_id = $null
$taskChoice.choice_id = 'choice_1'
$taskChoice.choice_revision = 1
$taskChoice.option_id = 'swift'
Test-DocumentCase -Name 'choice' -Value $taskChoice -Schema $taskBattle -Expected $true
$taskChoice.option_id = $null
Test-DocumentCase -Name 'choice-no-option' -Value $taskChoice -Schema $taskBattle -Expected $false
$taskBuffered = Copy-Document (Read-Document 'action-receipt.example.json')
$taskBuffered.status = 'buffered'
$taskBuffered.reason = 'buffered'
$taskBuffered.effective_frame = $null
Test-DocumentCase -Name 'buffered' -Value $taskBuffered -Schema $taskBattle -Expected $true
$taskBuffered.effective_frame = 300
Test-DocumentCase -Name 'buffered-effective-frame' -Value $taskBuffered -Schema $taskBattle -Expected $false
$taskResume = Read-Document 'resume.example.json'
$taskControlSchema = @{ '$schema' = 'https://json-schema.org/draft/2020-12/schema' } + $taskApi.components.schemas.PauseOrResumeControl
$taskResume.data.reason = 'focus_loss'
Test-DocumentCase -Name 'resume-focus-loss' -Value $taskResume -Schema $taskControlSchema -Expected $false
$taskResume = Read-Document 'resume.example.json'
$taskResume.data.pause_id = $null
Test-DocumentCase -Name 'resume-no-pause-id' -Value $taskResume -Schema $taskControlSchema -Expected $false
$taskFrame = Read-Document 'terminal-frame.example.json'
$taskFrameSchema = Read-Document 'frame.schema.json'
foreach ($name in @('zero-player', 'trial-false-none', 'terminal-no-summary', 'success-team-wipe')) {
    $changed = Copy-Document $taskFrame
    switch ($name) {
        'zero-player' { $changed.summary.participants[0].player_id = '0' }
        'trial-false-none' { $changed.summary.ineligible_reason = 'none' }
        'terminal-no-summary' { $changed.summary = $null }
        'success-team-wipe' { $changed.terminal.reason = 'team_wipe' }
    }
    Test-DocumentCase -Name $name -Value $changed -Schema $taskFrameSchema -Expected $false
}
$taskBaseline = Read-Document 'baseline.example.json'
$taskBaseline.own.weapon_states = @($taskBaseline.own.weapon_states[0])
Test-DocumentCase -Name 'baseline-one-weapon' -Value $taskBaseline -Schema $taskBattle -Expected $false
$taskParameters = Read-Document 'parameters.json'
$taskParameters.published = $true
Test-DocumentCase -Name 'parameters-published' -Value $taskParameters -Schema (Read-Document 'parameters.schema.json') -Expected $false
$taskDynamic = Read-Document 'dynamic.example.json'
$taskDynamic.group_kind = 'enemies'
Test-DocumentCase -Name 'wrong-dynamic-entity-kind' -Value $taskDynamic -Schema $taskBattle -Expected $false
$taskSemanticsText = & node (Join-Path $PSScriptRoot 'check-semantics.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Semantic document checks failed' }
$taskSemantics = $taskSemanticsText | ConvertFrom-Json -AsHashtable -Depth 100
@{
    status = 'PASS_DOCUMENTS_ONLY'
    standard_schema_validator = 'PowerShell Test-Json Draft2020-12'
    schema_positive_checks = $taskPositive
    schema_negative_checks = $taskNegative
    semantics = $taskSemantics
    game_or_services_started = $false
} | ConvertTo-Json -Depth 20
