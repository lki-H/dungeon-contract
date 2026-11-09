param(
    [ValidateSet('Checks','Build','Practice','Capture','ControlsCheck','UiCheck','LobbyCapture','Pressure','AuthorityPressure','AuthorityStandalone')][string]$Action='Checks',
    [ValidateSet('ranger','runesmith')][string]$Loadout='ranger',
    [ValidateRange(0,3600)][int]$DurationSeconds=0,
    [ValidateSet('mixed','ranger_fan','runesmith_twin','large_text','offscreen')][string]$PressureCase='mixed',
    [switch]$Smoke,
    [ValidateSet(1,3)][int]$Repetitions=3
)
. (Join-Path $PSScriptRoot 'dc-common.ps1')
. (Join-Path $PSScriptRoot 'dc-v8-observation.ps1')
. (Join-Path $PSScriptRoot 'dc-v8-unity-wait.ps1')
. (Join-Path $PSScriptRoot 'dc-v8-memory-admission.ps1')
if($Action -eq 'AuthorityPressure'){throw 'The inherited Editor pressure fixture does not represent v8 bodies/ballistics. Use AuthorityStandalone, starting with -Smoke, then -Repetitions 3.'}
Assert-DcLayout
$paths=Get-DcPaths
$buildRoot=Join-Path $paths.Cache 'builds\v8-p5'
$previous=@{DOTNET_PROCESSOR_COUNT=$env:DOTNET_PROCESSOR_COUNT}
$location=Get-Location
$mutex=[Threading.Mutex]::new($false,'Local\DungeonContractDemoVerification')
$acquired=$false
try {
    try {$acquired=$mutex.WaitOne(0)} catch [Threading.AbandonedMutexException] {$acquired=$true}
    if(!$acquired){throw 'Another Demo session or verification is running.'}
    if((Get-DcPlayState).State -ne 'stopped'){throw 'Stop the recorded Demo session first.'}
    if($Action-notin@('Practice','Capture')){Assert-DcV8MemoryAdmission -Workload $Action}
    foreach($entry in (Get-DcEnvironment).GetEnumerator()){$previous[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key,'Process');[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')}
    foreach($entry in @(Get-ChildItem Env: | Where-Object Name -Match 'PASSWORD|SECRET|TOKEN|API_KEY|^DC_ADMISSION_KEY$')){if(!$previous.ContainsKey($entry.Name)){$previous[$entry.Name]=$entry.Value};[Environment]::SetEnvironmentVariable($entry.Name,$null,'Process')}
    if($Action -in @('Checks','Build','AuthorityPressure')) {
        $env:DOTNET_PROCESSOR_COUNT='2'
        $dcNativeProbeFolder=$null
        if($Action -eq 'Checks'){
            $dcNativeProbeFolder=Join-Path $paths.Cache ('evidence/v8-probe-native-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
            Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_probe_evidence.py'),'begin','--kind','native','--output',$dcNativeProbeFolder) (Join-Path $paths.Cache 'logs/v8-probe-inputs.log')
        }
        if($Action -eq 'Build'){Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'begin',$buildRoot) (Join-Path $paths.Cache 'logs/v8-build-inputs.log')}
        $method=if($Action -eq 'Build'){'DungeonContract.Editor.DcBuild.Windows'}elseif($Action -eq 'AuthorityPressure'){'DungeonContract.Editor.DcP5AuthorityPressure.Run'}else{'DungeonContract.Editor.DcV8Checks.Run'}
        $dcUnityLog=Join-Path $paths.Cache ('logs\v8-p5-'+$Action.ToLowerInvariant()+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
        $arguments=@('-dc-runtime','v8','-projectPath',$paths.UnityProject,'-batchmode','-nographics','-job-worker-count','2','-quit','-executeMethod',$method,'-dcBuildRoot',$buildRoot,'-logFile',$dcUnityLog)
        $process=Start-Process -FilePath $paths.Unity -ArgumentList (Get-DcNativeArgumentString $arguments) -PassThru -WindowStyle Hidden
        Wait-DcV8Unity -Process $process -Log $dcUnityLog
        if($dcNativeProbeFolder){Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_probe_evidence.py'),'complete','--output',$dcNativeProbeFolder) (Join-Path $paths.Cache 'logs/v8-probe-result.log')}
        if($Action -eq 'Build'){Set-Location (Join-Path $paths.Project 'backend');Invoke-DcNative go @('build','-o',(Join-Path $buildRoot 'dc-control.exe'),'./cmd/server') (Join-Path $paths.Cache 'logs\v8-p5-control-build.log');Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'complete',$buildRoot) (Join-Path $paths.Cache 'logs/v8-build-receipt.log')}
    } else {
        Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'verify',$buildRoot) (Join-Path $paths.Cache 'logs/v8-p5-current-build-check.log')
        $stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
        $dcUiProbeFolder=$null
        if($Action -eq 'UiCheck'){
            $dcUiProbeFolder=Join-Path $paths.Cache ('evidence/v8-probe-ui-'+$Loadout+'-'+$stamp+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
            Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_probe_evidence.py'),'begin','--kind',('ui-'+$Loadout),'--output',$dcUiProbeFolder) (Join-Path $paths.Cache 'logs/v8-ui-probe-inputs.log')
        }
        Add-Type -AssemblyName System.Windows.Forms
        $area=[System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $dcP5Width=[int]([Math]::Floor([Math]::Min(1536,[Math]::Min($area.Width-32,($area.Height-64)*16/9))/16)*16)
        $dcP5Height=[int]($dcP5Width*9/16)
        $arguments=@('-dc-runtime','v8','-dc-stage','p4','-dc-p5','true','-dc-practice','true','-dc-loadout',$Loadout,'-force-d3d11','-screen-fullscreen','0','-screen-width',[string]$dcP5Width,'-screen-height',[string]$dcP5Height,'-dc-s1-metrics',(Join-Path $paths.Cache ('evidence\v8-p5-practice-'+$stamp+'.json')),'-logFile',(Join-Path $paths.Cache ('logs\v8-p5-practice-'+$stamp+'.log')))
        if($Action -eq 'ControlsCheck'){$arguments=@('-batchmode','-nographics','-dc-runtime','v8','-dc-p5','false','-dc-v5-controls-check','true','-dc-v5-controls-output',(Join-Path $paths.Cache 'evidence\v8-p5-controls.json'),'-logFile',(Join-Path $paths.Cache 'logs\v8-p5-controls.log'))}
        if($Action -eq 'AuthorityStandalone'){$arguments=@('-batchmode','-nographics','-dc-runtime','v8','-dc-p5','true','-dc-p5-authority-probe','true','-dc-p5-authority-output',(Join-Path $paths.Cache ('evidence\v8-p5-authority-standalone-'+$stamp+'.json')),'-logFile',(Join-Path $paths.Cache ('logs\v8-p5-authority-standalone-'+$stamp+'.log')))}
        if($Smoke){if($Action -ne 'AuthorityStandalone'){throw 'Smoke is only available for the isolated authority probe.'};$arguments+=@('-dc-v8-perf-smoke','true')}
        if($Action -eq 'AuthorityStandalone'){$arguments+=@('-dc-v8-perf-repetitions',[string]$Repetitions)}
        if($Action -eq 'UiCheck'){$arguments+=@('-dc-p5-ui-probe','true','-dc-settings-profile','validation')}
        if($Action -eq 'Pressure'){$dcPressureSeconds=if($DurationSeconds -ge 35){$DurationSeconds}else{150};$arguments+=@('-dc-p5-pressure','true','-dc-p5-pressure-case',$PressureCase,'-dc-p5-pressure-seconds',[string]$dcPressureSeconds,'-dc-p5-pressure-output',(Join-Path $paths.Cache ('evidence\v8-p5-render-pressure-'+$PressureCase+'-'+$stamp+'.json')),'-dc-settings-profile','validation')}
        if($Action -eq 'LobbyCapture'){$arguments=@('-dc-runtime','v8','-dc-p5','true','-dc-p5-lobby-capture',(Join-Path $paths.Cache ('evidence\v8-p5-lobby-'+$stamp+'.png')),'-screen-fullscreen','0','-screen-width',[string]$dcP5Width,'-screen-height',[string]$dcP5Height,'-logFile',(Join-Path $paths.Cache ('logs\v8-p5-lobby-'+$stamp+'.log')))}
        if($DurationSeconds -gt 0 -and $Action -ne 'Pressure'){$arguments+=@('-dc-s1-exit-after',[string]$DurationSeconds)}
        if($Action -eq 'Capture'){$arguments+=@('-dc-p1-capture',(Join-Path $paths.Cache ('evidence\v8-p5-current-'+$Loadout+'-'+$stamp+'.png')))}
        $windowStyle=if($Action -in @('ControlsCheck','AuthorityStandalone')){'Hidden'}else{'Normal'}
        $arguments=@('-job-worker-count','2')+$arguments
        $dcFingerprintFiles=@('DungeonContract/DungeonContract.exe','DungeonContract/DungeonContract_Data/Managed/Assembly-CSharp.dll','GameService/GameService_Data/Managed/Assembly-CSharp.dll','DungeonContract/Content/demo-v8-p5.json','DungeonContract/Content/parameters-v8-p5.json','DungeonContract/Content/world-v8-p5.json')
        $dcFingerprintRows=@(foreach($dcRelative in $dcFingerprintFiles){$dcFile=Get-Item -LiteralPath (Join-Path $buildRoot $dcRelative);[pscustomobject]@{path=$dcFile.FullName;bytes=$dcFile.Length;sha256=(Get-FileHash -LiteralPath $dcFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
        $dcFreeze=Join-Path $paths.Project 'docs/game-demo/v8-runtime-source-freeze.json'
        [IO.File]::WriteAllText((Join-Path $paths.Cache ('evidence/v8-'+$Action.ToLowerInvariant()+'-binaries-'+$stamp+'.json')),([pscustomobject]@{profile='dc_ruins.v8.p5.1';action=$Action;loadout=$Loadout;case=$PressureCase;smoke_only=[bool]$Smoke;repetitions=$Repetitions;requested_duration_seconds=$(if($Action -eq 'Pressure'){$dcPressureSeconds}else{$DurationSeconds});recorded_at=[DateTimeOffset]::Now.ToString('o');source='ordinary_Unity_build';runtime_source_freeze_sha256=$(if(Test-Path -LiteralPath $dcFreeze){(Get-FileHash -LiteralPath $dcFreeze -Algorithm SHA256).Hash.ToLowerInvariant()}else{$null});files=$dcFingerprintRows}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
        $process=Start-Process -FilePath (Join-Path $buildRoot 'DungeonContract\DungeonContract.exe') -ArgumentList (Get-DcNativeArgumentString $arguments) -PassThru -WindowStyle $windowStyle
        $null=$process.Handle
        if($Action -in @('ControlsCheck','UiCheck','LobbyCapture','Pressure','AuthorityStandalone')){$dcWaitSeconds=if($Action -eq 'AuthorityStandalone'){if($Smoke){90}else{1000}}elseif($Action -eq 'Pressure'){if($DurationSeconds -ge 35){$DurationSeconds+45}else{195}}elseif($Action -eq 'UiCheck'){100}else{60};Wait-DcV8ObservedProcess -Process $process -TimeoutSeconds $dcWaitSeconds -Output (Join-Path $paths.Cache ('evidence\v8-'+$Action.ToLowerInvariant()+'-memory-'+$stamp+'.json'))}else{$process.WaitForExit()}
        if($process.ExitCode -ne 0){throw 'P5 player failed; inspect its recorded log.'}
        if($dcUiProbeFolder){Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_probe_evidence.py'),'complete','--output',$dcUiProbeFolder,'--binary-manifest',(Join-Path $paths.Cache ('evidence/v8-uicheck-binaries-'+$stamp+'.json'))) (Join-Path $paths.Cache 'logs/v8-ui-probe-result.log')}
    }
} finally {
    Set-Location $location
    foreach($entry in $previous.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')}
    if($acquired){$mutex.ReleaseMutex()};$mutex.Dispose()
}
