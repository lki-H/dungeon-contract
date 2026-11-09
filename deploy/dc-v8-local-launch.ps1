param([switch]$CheckOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$dcProject=Split-Path -Parent $PSScriptRoot
foreach($dcRelative in @('deploy/portfolio.ps1','deploy/dc-v8-lan.ps1','docs/game-demo/config/demo-v8-p5.json')){
    if(!(Test-Path -LiteralPath (Join-Path $dcProject $dcRelative) -PathType Leaf)){throw ('Missing v8 launcher dependency: '+$dcRelative)}
}
if($CheckOnly){
    [pscustomobject]@{status='PASS_SOURCE_ENTRY_LINKS';profile='dc_ruins.v8.p5.1';services_started=$false;game_started=$false}|ConvertTo-Json -Compress
    return
}
Set-Location -LiteralPath $dcProject
& ./deploy/portfolio.ps1 -Action ServicesStart
& ./deploy/portfolio.ps1 -Action CheckServices
& ./deploy/dc-v8-lan.ps1 -Action Host -Runtime v8 -P5 -BindAddress 127.0.0.2 -AllowedPlayers 127.0.0.1 -LocalPlayer
