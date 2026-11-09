param([string]$HostAddress='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dc-native-arguments.ps1')
if(!$HostAddress){$HostAddress=Read-Host 'Host LAN or Radmin IPv4 (local test: 127.0.0.2)'}
$dcP5Address=$null
if(![Net.IPAddress]::TryParse($HostAddress,[ref]$dcP5Address) -or $dcP5Address.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or $dcP5Address.ToString() -ne $HostAddress){throw 'Use a canonical IPv4 address.'}
$dcP5Bytes=$dcP5Address.GetAddressBytes()
if(!($dcP5Bytes[0] -in @(10,26,127) -or ($dcP5Bytes[0] -eq 172 -and $dcP5Bytes[1] -ge 16 -and $dcP5Bytes[1] -le 31) -or ($dcP5Bytes[0] -eq 192 -and $dcP5Bytes[1] -eq 168))){throw 'Only local, LAN or Radmin IPv4 is allowed.'}
$dcP5Executable=Join-Path $PSScriptRoot 'DungeonContract.exe'
if(!(Test-Path -LiteralPath $dcP5Executable -PathType Leaf)){throw 'Keep this launcher inside the complete P5 portable player folder.'}
Add-Type -AssemblyName System.Windows.Forms
$dcP5Area=[System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$dcP5Width=[int]([Math]::Floor([Math]::Min(1536,[Math]::Min($dcP5Area.Width-32,($dcP5Area.Height-64)*16/9))/16)*16)
$dcP5Height=[int]($dcP5Width*9/16)
$dcP5Log=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DungeonContract/V8/logs'
[void][IO.Directory]::CreateDirectory($dcP5Log)
$dcP5LogFile=Join-Path $dcP5Log ((Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'.log')
try{
    $dcP5PreviousCredentials=@{}
    foreach($dcP5Entry in @(Get-ChildItem Env: | Where-Object Name -Match 'PASSWORD|SECRET|TOKEN|API_KEY|^DC_ADMISSION_KEY$')){
        $dcP5PreviousCredentials[$dcP5Entry.Name]=$dcP5Entry.Value
        [Environment]::SetEnvironmentVariable($dcP5Entry.Name,$null,'Process')
    }
    $dcP5Process=Start-Process -FilePath $dcP5Executable -WorkingDirectory $PSScriptRoot -ArgumentList (Get-DcNativeArgumentString @('-job-worker-count','2','-dc-runtime','v8','-dc-p5','true','-dc-server-host',$HostAddress,'-force-d3d11','-screen-fullscreen','0','-screen-width',[string]$dcP5Width,'-screen-height',[string]$dcP5Height,'-logFile',$dcP5LogFile)) -PassThru -WindowStyle Normal
    $null=$dcP5Process.Handle
}catch{
    throw ('P5 player could not start ('+$_.Exception.GetType().Name+'). Keep the complete extracted player folder. Log: '+$dcP5LogFile)
}finally{
    foreach($dcP5Entry in $dcP5PreviousCredentials.GetEnumerator()){
        [Environment]::SetEnvironmentVariable($dcP5Entry.Key,$dcP5Entry.Value,'Process')
    }
}
$dcP5Process.WaitForExit()
$dcP5ExitCode=$dcP5Process.ExitCode
if($null -eq $dcP5ExitCode){throw ('P5 player exit status could not be confirmed. This is not a confirmed game crash. Log: '+$dcP5LogFile)}
if($dcP5ExitCode -ne 0){throw ('P5 player exited with code '+$dcP5ExitCode+' ('+('0x{0:X8}' -f $dcP5ExitCode)+'). Log: '+$dcP5LogFile)}
