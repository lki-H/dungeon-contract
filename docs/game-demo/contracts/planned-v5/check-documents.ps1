[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$dc22Cache = 'D:/dev-cache/dungeon-contract/tmp/docs-v5-schema'
$null = New-Item -ItemType Directory -Path $dc22Cache -Force
$dc22Positive = 0
$dc22Negative = 0
function Read-Plan {
    param([string]$Name)
    Get-Content -LiteralPath (Join-Path $PSScriptRoot $Name) -Raw | ConvertFrom-Json -AsHashtable -Depth 100
}
function Copy-Plan {
    param([object]$Value)
    ConvertTo-Json -InputObject $Value -Depth 100 | ConvertFrom-Json -AsHashtable -Depth 100
}
function Test-Plan {
    param([string]$Name, [object]$Value, [object]$Schema, [bool]$Expected)
    $dc22DocumentPath = Join-Path $dc22Cache ($Name + '.json')
    $dc22SchemaPath = Join-Path $dc22Cache ($Name + '.schema.json')
    [IO.File]::WriteAllText($dc22DocumentPath, (ConvertTo-Json -InputObject $Value -Depth 100))
    [IO.File]::WriteAllText($dc22SchemaPath, (ConvertTo-Json -InputObject $Schema -Depth 100))
    $dc22Actual = Test-Json -LiteralPath $dc22DocumentPath -SchemaFile $dc22SchemaPath -ErrorAction SilentlyContinue
    if ($dc22Actual -ne $Expected) { throw "Schema case $Name expected=$Expected actual=$dc22Actual" }
    if ($Expected) { $script:dc22Positive++ } else { $script:dc22Negative++ }
}
$dc22Parameters = Read-Plan 'parameters.json'
$dc22ParameterSchema = Read-Plan 'parameters.schema.json'
$dc22Battle = Read-Plan 'battle.schema.json'
$dc22Frame = Read-Plan 'frame.schema.json'
$dc22Api = Read-Plan 'openapi.json'
Test-Plan -Name 'parameters' -Value $dc22Parameters -Schema $dc22ParameterSchema -Expected $true
Test-Plan -Name 'terminal' -Value (Read-Plan 'terminal-frame.example.json') -Schema $dc22Frame -Expected $true
$dc22Examples = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.example.json' -File
foreach ($dc22Example in $dc22Examples) {
    if ($dc22Example.Name -eq 'terminal-frame.example.json') { continue }
    $dc22Schema = if ($dc22Example.Name -in @('pause.example.json', 'resume.example.json')) {
        @{ '$schema' = 'https://json-schema.org/draft/2020-12/schema' } + $dc22Api.components.schemas.PauseOrResumeControl
    } else { $dc22Battle }
    Test-Plan -Name $dc22Example.Name -Value (Read-Plan $dc22Example.Name) -Schema $dc22Schema -Expected $true
}
foreach ($dc22Mutation in @(
    @{ Field = 'protocol_version'; Value = 4 },
    @{ Field = 'connection_generation'; Value = 0 },
    @{ Field = 'input_sequence'; Value = -1 },
    @{ Field = 'aim_u16'; Value = 65536 },
    @{ Field = 'damage'; Value = 999 },
    @{ Field = 'operation'; Value = 'dc_ruins.v4' }
)) {
    $dc22Changed = Copy-Plan (Read-Plan 'input.example.json')
    $dc22Changed[$dc22Mutation.Field] = $dc22Mutation.Value
    Test-Plan -Name ('bad-input-' + $dc22Mutation.Field) -Value $dc22Changed -Schema $dc22Battle -Expected $false
}
$dc22Changed = Copy-Plan (Read-Plan 'charge-release.example.json')
$dc22Changed.data.Remove('episode_id')
Test-Plan -Name 'missing-charge-episode' -Value $dc22Changed -Schema $dc22Battle -Expected $false
$dc22Changed = Copy-Plan (Read-Plan 'attack.example.json')
$dc22Changed.data.target_damage = 999
Test-Plan -Name 'client-damage-injection' -Value $dc22Changed -Schema $dc22Battle -Expected $false
$dc22Changed = Copy-Plan (Read-Plan 'emitter.example.json')
$dc22Changed.data.reserved_remaining = 241
Test-Plan -Name 'oversized-emitter-reservation' -Value $dc22Changed -Schema $dc22Battle -Expected $false
$dc22Changed = Copy-Plan (Read-Plan 'summon.example.json')
$dc22Changed.data.contract_progress_eligible = $true
Test-Plan -Name 'summon-positive-contract-flag' -Value $dc22Changed -Schema $dc22Battle -Expected $false
$dc22Changed = Copy-Plan (Read-Plan 'baseline.example.json')
$dc22Changed.own.weapon_revision = 0
Test-Plan -Name 'baseline-weapon-revision' -Value $dc22Changed -Schema $dc22Battle -Expected $false
$dc22Changed = Copy-Plan (Read-Plan 'baseline.example.json')
$dc22Changed.peers = @(@{
    player_id = '102'; slot = 1; room_id = 'entry'; life_state = 'alive'; health = 100; max_health = 100
    contract_category = 'damage'; position_milli = $null; private_contract_reward = 999
})
Test-Plan -Name 'peer-private-field' -Value $dc22Changed -Schema $dc22Battle -Expected $false
$dc22Changed = Copy-Plan (Read-Plan 'projectile-dynamic.example.json')
$dc22Changed.group_id = 64
Test-Plan -Name 'dynamic-group-budget' -Value $dc22Changed -Schema $dc22Battle -Expected $false
$dc22Changed = Copy-Plan $dc22Parameters
$dc22Changed.published = $true
Test-Plan -Name 'published-plan' -Value $dc22Changed -Schema $dc22ParameterSchema -Expected $false
$dc22Changed = Copy-Plan $dc22Parameters
$dc22Changed.growth[6].charge_full_delta_ms = -200
Test-Plan -Name 'obsolete-blade-charge-growth' -Value $dc22Changed -Schema $dc22ParameterSchema -Expected $false
$dc22Changed = Copy-Plan (Read-Plan 'pause.example.json')
$dc22Changed.schema_version = 5
Test-Plan -Name 'wrong-control-schema' -Value $dc22Changed -Schema $dc22Api.components.schemas.PauseOrResumeControl -Expected $false
$dc22Changed = Copy-Plan (Read-Plan 'terminal-frame.example.json')
$dc22Changed.schema_version = 4
Test-Plan -Name 'wrong-fact-version' -Value $dc22Changed -Schema $dc22Frame -Expected $false
@{
    scope = 'documentation_only'
    validator = 'PowerShell Test-Json Draft2020-12'
    schema_positive = $dc22Positive
    schema_negative = $dc22Negative
    runtime_or_gameplay_tested = $false
} | ConvertTo-Json
& node (Join-Path $PSScriptRoot 'check-semantics.mjs')
if ($LASTEXITCODE -ne 0) { throw 'v5 semantic document checks failed' }
