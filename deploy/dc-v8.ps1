param(
    [ValidateSet('Checks','Regression','Build','ControlsCheck','Practice','Capture','Test','Integration','IntegrationAll','TransferPreview','Verify','VerifyFull','VerifyPressure','VerifyLobby','Solo')][string]$Action='Checks',
    [ValidateSet('ranger','runesmith')][string]$Loadout='ranger',
    [ValidateSet(1,2,4)][int]$Players=1,
    [ValidateSet('success','wipe','rescue')][string]$Scenario='success',
    [string]$Contract='',
    [ValidateRange(0,3600)][int]$DurationSeconds=0,
    [switch]$UniformLoadout,
    [switch]$P5,
    [switch]$Trial,
    [switch]$Headless,
    [ValidateRange(-1,4)][int]$RenderClients=-1,
    [ValidateSet('direct','mild','adverse')][string]$UdpProfile='direct'
)
$P5=$true
if($DurationSeconds -gt 0 -and $Action -notin @('Practice','Capture')){throw 'DurationSeconds applies only to Practice/Capture. Verify is a bounded action probe; VerifyFull uses its explicit 35-minute harness deadline and does not accept a shortened success requirement.'}
. (Join-Path $PSScriptRoot 'dc-common.ps1')
. (Join-Path $PSScriptRoot 'dc-v8-unity-wait.ps1')
. (Join-Path $PSScriptRoot 'dc-v8-memory-admission.ps1')
if($Scenario -eq 'rescue' -and $Action -in @('VerifyFull','VerifyPressure')){throw 'Rescue is a bounded input probe: use -Action Verify -Players 2 -Scenario rescue.'}
if($Headless -and $Action -notin @('VerifyFull','VerifyPressure')){throw 'Headless applies only to the real full-flow or combat-pressure input harness.'}
if($RenderClients -ge 0 -and ($Action -ne 'VerifyFull' -or $Headless -or $RenderClients -gt $Players)){throw 'RenderClients requires VerifyFull, must not exceed Players, and cannot combine with Headless.'}
if($UdpProfile -ne 'direct' -and $Action -notin @('VerifyFull','VerifyPressure')){throw 'UDP conditioning applies only to automated full-flow or combat-pressure verification.'}
Assert-DcLayout
$paths=Get-DcPaths
$previous=@{}
foreach($name in @('DOTNET_PROCESSOR_COUNT','GOMAXPROCS','DC_CONFIG_PATH','DC_INTEGRATION','DC_V3_P0_CONFIRM')) {$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$location=Get-Location
$mutex=[Threading.Mutex]::new($false,'Local\DungeonContractDemoVerification')
$acquired=$false
try {
    try {$acquired=$mutex.WaitOne(0)} catch [Threading.AbandonedMutexException] {$acquired=$true}
    if (!$acquired) {throw 'Another Demo session or verification is running.'}
    if ((Get-DcPlayState).State -ne 'stopped') {throw 'Stop the recorded Demo session first.'}
    if($Action-notin@('Practice','Capture','TransferPreview')){
        $dcRenderBudget=if($Action-eq'Solo'){1}elseif($Action-eq'VerifyLobby'){1}elseif($Action-eq'VerifyFull'-and !$Headless){if($RenderClients-ge0){$RenderClients}else{$Players}}elseif($Action-eq'VerifyPressure'-and !$Headless){$Players}else{0}
        Assert-DcV8MemoryAdmission -Workload $Action -Players $Players -RenderClients $dcRenderBudget
    }
    $values=Get-DcEnvironment -IncludeCredentials:($Action -in @('Integration','IntegrationAll','TransferPreview','Verify','VerifyFull','VerifyPressure','VerifyLobby','Solo'))
    $env:GOMAXPROCS='2'
    foreach ($entry in $values.GetEnumerator()) {if(!$previous.ContainsKey($entry.Key)){$previous[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key,'Process')};[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')}
    $env:DC_CONFIG_PATH=Join-Path $paths.Project $(if($P5){'docs\game-demo\config\demo-v8-p5.json'}else{'docs\game-demo\config\demo-v8-p5.json'})
    if($Action -in @('Build','Checks','Regression','ControlsCheck','Practice','Capture','Test')) {foreach($entry in @(Get-ChildItem Env: | Where-Object Name -Match 'PASSWORD|SECRET|TOKEN|API_KEY|^DC_ADMISSION_KEY$')) {if(!$previous.ContainsKey($entry.Name)){$previous[$entry.Name]=$entry.Value};[Environment]::SetEnvironmentVariable($entry.Name,$null,'Process')}}
    $buildRoot=Join-Path $paths.Cache $(if($P5){'builds\v8-p5'}else{'builds\v8-p5'})
    if($Action -eq 'Checks'){
        $previous['DC_V8_MANIFEST_MODEL_FILE']=[Environment]::GetEnvironmentVariable('DC_V8_MANIFEST_MODEL_FILE','Process')
        $env:DC_V8_MANIFEST_MODEL_FILE=Join-Path $paths.Cache 'evidence/v8-go-manifest-model.json'
        Set-Location (Join-Path $paths.Project 'backend')
        Invoke-DcNative go @('test','-p','1','-count=1','-run','TestV8FrozenContentAndIdentity','./internal/pve/gameplay') (Join-Path $paths.Cache 'logs/v8-go-manifest-model.log')
        Set-Location $paths.Project
    }
    if ($Action -in @('Build','Checks','Regression')) {
        $env:DOTNET_PROCESSOR_COUNT='2'
        $dcProbeFolder=$null
        if($Action -in @('Checks','Regression')){
            $dcProbeKind=if($Action -eq 'Checks'){'native'}else{'compatibility'}
            $dcProbeFolder=Join-Path $paths.Cache ('evidence/v8-probe-'+$dcProbeKind+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
            Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_probe_evidence.py'),'begin','--kind',$dcProbeKind,'--output',$dcProbeFolder) (Join-Path $paths.Cache 'logs/v8-probe-inputs.log')
        }
        if($Action -eq 'Build'){Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'begin',$buildRoot) (Join-Path $paths.Cache 'logs/v8-build-inputs.log')}
        $method=if ($Action -eq 'Build') {'DungeonContract.Editor.DcBuild.Windows'} elseif($Action -eq 'Regression') {'DungeonContract.Editor.DcV8Checks.Regression'} else {'DungeonContract.Editor.DcV8Checks.Run'}
        $dcUnityLog=Join-Path $paths.Cache ('logs\unity-v8-'+$Action.ToLowerInvariant()+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'.log')
        $arguments=@('-dc-runtime','v8','-projectPath',$paths.UnityProject,'-batchmode','-nographics','-job-worker-count','2','-quit','-executeMethod',$method,'-dcBuildRoot',$buildRoot,'-logFile',$dcUnityLog)
        if($Action -eq 'Regression') {$arguments+=@('-dc-p1-check-output',(Join-Path $paths.Cache 'evidence\v8-regression-p1.json'))}
        $process=Start-Process -FilePath $paths.Unity -ArgumentList (Get-DcNativeArgumentString $arguments) -PassThru -WindowStyle Hidden
        $null=$process.Handle
        Wait-DcV8Unity -Process $process -Log $dcUnityLog
        if($dcProbeFolder){Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_probe_evidence.py'),'complete','--output',$dcProbeFolder) (Join-Path $paths.Cache 'logs/v8-probe-result.log')}
        if($Action -eq 'Build'){Set-Location (Join-Path $paths.Project 'backend');Invoke-DcNative go @('build','-o',(Join-Path $buildRoot 'dc-control.exe'),'./cmd/server') (Join-Path $paths.Cache 'logs\v8-control-build.log');Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'complete',$buildRoot) (Join-Path $paths.Cache 'logs/v8-build-receipt.log')}
    } elseif($Action -eq 'ControlsCheck') {
        Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'verify',$buildRoot) (Join-Path $paths.Cache 'logs/v8-controls-build-check.log')
        $arguments=@('-batchmode','-nographics','-dc-runtime','v8','-dc-v5-controls-check','true','-dc-v5-controls-output',(Join-Path $paths.Cache 'evidence\v8-controls.json'),'-logFile',(Join-Path $paths.Cache 'logs\v8-controls.log'))
        $process=Start-Process -FilePath (Join-Path $buildRoot 'DungeonContract\DungeonContract.exe') -ArgumentList (Get-DcNativeArgumentString $arguments) -PassThru -WindowStyle Hidden
        $null=$process.Handle
        if(!$process.WaitForExit(60000)){Stop-Process -InputObject $process -ErrorAction SilentlyContinue;throw 'V8 input probe timed out; its owned process was stopped.'}
        if($process.ExitCode -ne 0){throw 'V8 input probe failed.'}
    } elseif($Action -in @('Practice','Capture')) {
        Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'verify',$buildRoot) (Join-Path $paths.Cache 'logs/v8-practice-build-check.log')
        $stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
        Add-Type -AssemblyName System.Windows.Forms
        $area=[System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $dcPracticeWidth=[int]([Math]::Floor([Math]::Min(1536,[Math]::Min($area.Width-32,($area.Height-64)*16/9))/16)*16)
        $dcPracticeHeight=[int]($dcPracticeWidth*9/16)
        $arguments=@('-dc-runtime','v8','-dc-stage','p4','-dc-practice','true','-dc-loadout',$Loadout,'-force-d3d11','-screen-fullscreen','0','-screen-width',[string]$dcPracticeWidth,'-screen-height',[string]$dcPracticeHeight,'-dc-s1-metrics',(Join-Path $paths.Cache ('evidence\v8-practice-'+$stamp+'.json')),'-logFile',(Join-Path $paths.Cache ('logs\v8-practice-'+$stamp+'.log')))
        if($DurationSeconds -gt 0){$arguments+=@('-dc-s1-exit-after',[string]$DurationSeconds)}
        if($Action -eq 'Capture'){$arguments+=@('-dc-p1-capture',(Join-Path $paths.Cache ('evidence\v8-current-'+$Loadout+'.png')))}
        $arguments=@('-job-worker-count','2')+$arguments
        $process=Start-Process -FilePath (Join-Path $buildRoot 'DungeonContract\DungeonContract.exe') -ArgumentList (Get-DcNativeArgumentString $arguments) -PassThru -WindowStyle Normal
        $null=$process.Handle
        $process.WaitForExit()
        if($process.ExitCode -ne 0){throw 'V8 practice failed.'}
    } else {
        Set-Location (Join-Path $paths.Project 'backend')
        if ($Action -eq 'Migrate') {
            Assert-DcCompose
            $env:DC_V3_P0_CONFIRM='I_UNDERSTAND_DC_V3_P0_MIGRATION'
            Invoke-DcNative go @('run','./cmd/tools/v2_migrate','-stage','v4-p3') (Join-Path $paths.Cache 'logs\v8-existing-day46-check.log')
        } elseif($Action -eq 'TransferPreview') {
            Assert-DcCompose
            Invoke-DcNative go @('run','./cmd/tools/dc_p3_progress') (Join-Path $paths.Cache 'logs\v8-transfer-preview.json')
        } elseif ($Action -in @('Test','Integration','IntegrationAll')) {
            if ($Action -in @('Integration','IntegrationAll')) {Assert-DcCompose;$env:DC_INTEGRATION='1'}
            $dcGoTests=if($Action -eq 'IntegrationAll'){@('test','-p','1','-count=1','-v','./internal/pve/...','./internal/dclan','./cmd/tools/dc_verify','./cmd/tools/dc_lan_player')}else{@('test','-p','1','-count=1','-run','TestV8|TestV6|TestV5|TestP4|TestP3','-v','./internal/pve/run','./internal/pve/gameplay','./internal/pve/v3','./cmd/tools/dc_verify')}
            Invoke-DcNative go $dcGoTests (Join-Path $paths.Cache ('logs\v8-'+$Action.ToLowerInvariant()+'.log'))
        } else {
            Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'verify',$buildRoot) (Join-Path $paths.Cache 'logs/v8-runtime-build-check.log')
            Assert-DcCompose
            $arguments=@('run','-p','1','./cmd/tools/dc_verify','-p4','-v8','-loadout',$Loadout,'-players',[string]$Players,'-scenario',$Scenario)
            if($Contract){$arguments+=@('-contract',$Contract)}
            if($P5){$arguments+='-p5'}
            if($Action -eq 'VerifyLobby'){if(!$P5){throw 'VerifyLobby requires P5.'};$arguments+='-p5-lobby'}
            if($Trial){$arguments+='-trial'}
            if($UniformLoadout){$arguments+='-uniform-loadout'}
            if($UdpProfile -ne 'direct'){$arguments+=@('-udp-profile',$UdpProfile)}
            if ($Action -eq 'VerifyFull') {$arguments+='-full-flow';if(!$Headless){$arguments+='-visual'}}
            if($RenderClients -ge 0){$arguments+=@('-render-clients',[string]$RenderClients)}
            if ($Action -eq 'VerifyPressure') {$arguments+='-combat-pressure';if(!$Headless){$arguments+='-visual'}}
            if ($Action -eq 'Solo') {Add-Type -AssemblyName System.Windows.Forms;$area=[System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea;$dcP3Width=[int]([Math]::Floor([Math]::Min(1536,[Math]::Min($area.Width-32,($area.Height-64)*16/9))/16)*16);$dcP3Height=[int]($dcP3Width*9/16);$arguments+=@('-human','-window-width',[string]$dcP3Width,'-window-height',[string]$dcP3Height)}
            Invoke-DcNative go $arguments (Join-Path $paths.Cache ('logs\v8-udp-'+$Loadout+'.log'))
        }
    }
} finally {
    Set-Location $location
    foreach ($entry in $previous.GetEnumerator()) {[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')}
    if ($acquired) {$mutex.ReleaseMutex()};$mutex.Dispose()
}
