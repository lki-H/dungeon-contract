param([string]$BindAddress='', [string[]]$AllowedPlayers=@())
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(!$BindAddress){$BindAddress=Read-Host 'This computer Radmin/LAN IPv4'}
if($AllowedPlayers.Count -eq 0){
    $dcPeers=Read-Host 'Allowed player IPv4s, comma separated, including yourself'
    $AllowedPlayers=@($dcPeers.Split(',')|ForEach-Object {$_.Trim()})
}
Write-Host 'Docker Desktop and the existing Demo MySQL/Redis containers must be healthy.'
Write-Host 'This entry starts host roles only. Open the complete v8 player folder separately.'
& (Join-Path $PSScriptRoot 'dc-v8-lan.ps1') -Action Host -Runtime v8 -P5 -BindAddress $BindAddress -AllowedPlayers $AllowedPlayers
