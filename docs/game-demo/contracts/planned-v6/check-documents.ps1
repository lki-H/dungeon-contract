[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$positive=0
$negative=0
function Check-Value([object]$Value,[string]$Schema,[bool]$Expected) {
    $json=ConvertTo-Json -InputObject $Value -Depth 100 -Compress
    $actual=Test-Json -Json $json -SchemaFile (Join-Path $PSScriptRoot $Schema) -ErrorAction SilentlyContinue
    if($actual -ne $Expected){throw ('Schema expectation failed: '+$Schema+' expected='+$Expected)}
    if($Expected){$script:positive++}else{$script:negative++}
}
function Clone-Value([object]$Value){ConvertTo-Json -InputObject $Value -Depth 100|ConvertFrom-Json -AsHashtable -Depth 100}
$index=Get-Content (Join-Path $PSScriptRoot 'examples-index.json') -Raw|ConvertFrom-Json -AsHashtable
foreach($entry in $index.examples){
    $value=Get-Content (Join-Path $PSScriptRoot $entry.path) -Raw|ConvertFrom-Json -AsHashtable -Depth 100
    Check-Value $value $entry.schema $true
    $changed=Clone-Value $value
    if($changed.ContainsKey('protocol_version')){$changed.protocol_version=5}else{$changed.schema_version=5}
    Check-Value $changed $entry.schema $false
    $changed=Clone-Value $value
    $changed.damage=999
    Check-Value $changed $entry.schema $false
}
foreach($name in @('parameters','world')){
    $value=Get-Content (Join-Path $PSScriptRoot ($name+'.json')) -Raw|ConvertFrom-Json -AsHashtable -Depth 100
    Check-Value $value ($name+'.schema.json') $true
    $changed=Clone-Value $value
    $changed.published=$true
    Check-Value $changed ($name+'.schema.json') $false
}
$baseline=Get-Content (Join-Path $PSScriptRoot 'baseline.example.json') -Raw|ConvertFrom-Json -AsHashtable -Depth 100
$changed=Clone-Value $baseline
$changed.own.protection_sources.Remove('rescue_until_game_ms')
Check-Value $changed 'battle.schema.json' $false
$changed=Clone-Value $baseline
$changed.own.attack_state.projectile_clear_shape='full_sector'
Check-Value $changed 'battle.schema.json' $false
$changed=Clone-Value $baseline
$changed.observed_room.traps=@(1..25|ForEach-Object{@{trap_id=('trap_'+$_);state='idle';phase_end_game_ms=0;position_milli=@{x=0;y=0};group='A';activated_at_game_ms=$null;activation_generation=1;cycle_index=0;radius_milli_tiles=700}})
Check-Value $changed 'battle.schema.json' $false
$events=Get-Content (Join-Path $PSScriptRoot 'projectile-lifecycle.example.json') -Raw|ConvertFrom-Json -AsHashtable -Depth 100
$changed=Clone-Value $events
$changed.events[2].reason='client_said_hit'
Check-Value $changed 'battle.schema.json' $false
$changed=Clone-Value $events
$changed.events[1].bounces_remaining=1
Check-Value $changed 'battle.schema.json' $false
[pscustomobject]@{status='PASS_DOCUMENT_SCHEMAS_ONLY';positive=$positive;negative=$negative;runtime_tested=$false;human_tested=$false}|ConvertTo-Json
