param(
    [ValidateSet('Build','Host','Check')][string]$Action='Check',
    [string]$BindAddress='',
    [string[]]$AllowedPlayers=@(),
    [ValidateRange(0,3600)][int]$RunSeconds=0,
    [ValidateSet('v8')][string]$Runtime='v8',
    [switch]$P5=$true,
    [switch]$LocalPlayer,
    [string]$VerifiedBuildEvidence=''
)
. (Join-Path $PSScriptRoot 'dc-common.ps1')
. (Join-Path $PSScriptRoot 'dc-v8-unity-wait.ps1')
. (Join-Path $PSScriptRoot 'dc-v8-memory-admission.ps1')
Assert-DcLayout
if(!$P5){throw "The v8 entry requires its P5 profile."}
if(($P5 -or $LocalPlayer) -and $Runtime -ne 'v8'){throw 'P5 requires Runtime v8.'}
if($LocalPlayer -and !$P5){throw 'The Unity lobby local player entry requires P5.'}
$paths=Get-DcPaths
$dcOriginal=@{DOTNET_PROCESSOR_COUNT=$env:DOTNET_PROCESSOR_COUNT;GOMAXPROCS=$env:GOMAXPROCS};$dcLocation=Get-Location
$dcLock=[Threading.Mutex]::new($false,'Local\DungeonContractDemoVerification')
$dcLocked=$false
$dcProcesses=[Collections.Generic.List[Diagnostics.Process]]::new()
try {
    if($Action -ne 'Check'){
        try{$dcLocked=$dcLock.WaitOne(0)}catch [Threading.AbandonedMutexException]{$dcLocked=$true}
        if(!$dcLocked -or (Get-DcPlayState).State -ne 'stopped'){throw 'Stop the other Demo session or verification first.'}
        Assert-DcV8MemoryAdmission -Workload $(if($Action-eq'Build'){if($VerifiedBuildEvidence){'PackageBuild'}else{'Build'}}elseif($LocalPlayer){'VerifyLobby'}else{'LanHost'})
    }
    $env:GOMAXPROCS='2'
    $dcValues=Get-DcEnvironment -IncludeCredentials:($Action -eq 'Host')
    foreach($entry in $dcValues.GetEnumerator()){$dcOriginal[$entry.Key]=[Environment]::GetEnvironmentVariable($entry.Key,'Process');[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')}
    $env:DC_CONFIG_PATH=Join-Path $paths.Project ('docs/game-demo/config/demo-'+$Runtime+'-p4.json')
    if($P5){$env:DC_CONFIG_PATH=Join-Path $paths.Project 'docs/game-demo/config/demo-v8-p5.json'}
    $dcPackageStage=if($Runtime -eq 'v8'){'v8-lan'}else{'p4-lan'}
    if($P5){$dcPackageStage='v8-p5-lan'}
    $dcRoot=Join-Path $paths.Cache ('builds/'+$dcPackageStage)
    if($Action -eq 'Build') {
        $env:DOTNET_PROCESSOR_COUNT='2'
        [void][IO.Directory]::CreateDirectory($dcRoot)
        foreach($entry in @(Get-ChildItem Env:|Where-Object Name -Match 'PASSWORD|SECRET|TOKEN|API_KEY|^DC_ADMISSION_KEY$')){if(!$dcOriginal.ContainsKey($entry.Name)){$dcOriginal[$entry.Name]=$entry.Value};[Environment]::SetEnvironmentVariable($entry.Name,$null,'Process')}
        $dcBuildSource='fresh_unity_build'
        Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'begin',$dcRoot) (Join-Path $paths.Cache 'logs/v8-lan-build-inputs.log')
        if($VerifiedBuildEvidence) {
            Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'verify',(Join-Path $paths.Cache 'builds/v8-p5')) (Join-Path $paths.Cache 'logs/v8-lan-parent-check.log')
            # Repackage only binaries pinned by a previous real Run; never call this a new compile.
            $dcProofPath=[IO.Path]::GetFullPath($VerifiedBuildEvidence)
            $dcProofRoot=[IO.Path]::GetFullPath((Join-Path $paths.Cache 'evidence'))+[IO.Path]::DirectorySeparatorChar
            if(!$dcProofPath.StartsWith($dcProofRoot,[StringComparison]::OrdinalIgnoreCase) -or (Get-Item -LiteralPath $dcProofPath).Length -gt 524288){throw 'Use an existing bounded real-Run manifest or successful build receipt in the Demo evidence directory.'}
            $dcProof=[IO.File]::ReadAllText($dcProofPath)|ConvertFrom-Json
            if($dcProof.profile -ne 'dc_ruins.v8.p5.1'){throw 'Verified binary profile does not match the current candidate.'}
            $dcExisting=Join-Path $paths.Cache 'builds/v8-p5'
            $dcRolePaths=@{control='dc-control.exe';authority_managed='GameService/GameService_Data/Managed/Assembly-CSharp.dll';client_managed='DungeonContract/DungeonContract_Data/Managed/Assembly-CSharp.dll'}
            $dcReceiptProof=$null -ne $dcProof.PSObject.Properties['status'] -and $dcProof.status -eq 'SUCCESSFUL_BUILD_BOUND_TO_INPUTS'
            if($dcReceiptProof -and (Get-FileHash -LiteralPath $dcProofPath -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $dcExisting 'v8-build-receipt.json') -Algorithm SHA256).Hash){throw 'Build receipt is not the currently verified ordinary build.'}
            foreach($dcRole in $(if($dcReceiptProof){@()}else{$dcRolePaths.Keys})) {
                $dcFile=Join-Path $dcExisting $dcRolePaths[$dcRole]
                $dcRows=@($dcProof.files|Where-Object role -EQ $dcRole)
                if($dcRows.Count -ne 1 -or [IO.Path]::GetFullPath($dcRows[0].path) -ne [IO.Path]::GetFullPath($dcFile) -or (Get-FileHash -LiteralPath $dcFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dcRows[0].sha256){throw ('Verified build fingerprint mismatch: '+$dcRole)}
            }
            foreach($dcRole in @('DungeonContract','GameService')) {
                foreach($dcConfigFile in @('demo-v8-p5.json','parameters-v8-p5.json','world-v8-p5.json')) {
                    $dcBundled=Join-Path $dcExisting ($dcRole+'/Content/'+$dcConfigFile)
                    $dcCurrent=Join-Path $paths.Project ('docs/game-demo/config/'+$dcConfigFile)
                    if((Get-FileHash -LiteralPath $dcBundled -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $dcCurrent -Algorithm SHA256).Hash){throw ('Verified bundle configuration mismatch: '+$dcConfigFile)}
                }
                [void][IO.Directory]::CreateDirectory((Join-Path $dcRoot $dcRole))
                foreach($dcItem in Get-ChildItem -LiteralPath (Join-Path $dcExisting $dcRole)) {
                    if($dcItem.Name -like '*DoNotShip*'){continue}
                    Copy-Item -LiteralPath $dcItem.FullName -Destination (Join-Path $dcRoot $dcRole) -Recurse -Force
                }
            }
            $dcBuildSource=if($dcReceiptProof){'repackaged_successful_ordinary_build_receipt_without_recompilation'}else{'repackaged_verified_players_without_recompilation'}
            Copy-Item -LiteralPath $dcProofPath -Destination (Join-Path $dcRoot 'verified-player-binaries.json')
        } else {
            $dcUnityLog=Join-Path $paths.Cache ('logs/unity-v8-lan-build-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
            $dcBuild=Start-Process -FilePath $paths.Unity -ArgumentList (Get-DcNativeArgumentString @('-dc-runtime','v8','-projectPath',$paths.UnityProject,'-batchmode','-nographics','-job-worker-count','2','-quit','-executeMethod','DungeonContract.Editor.DcBuild.Windows','-dcBuildRoot',$dcRoot,'-logFile',$dcUnityLog)) -WindowStyle Hidden -PassThru
            Wait-DcV8Unity -Process $dcBuild -Log $dcUnityLog
        }
        Set-Location (Join-Path $paths.Project 'backend')
        foreach($dcTool in @('dc_lan_gateway','dc_lan_player')){
            Invoke-DcNative go @('build','-trimpath','-o',(Join-Path $dcRoot ($dcTool.Replace('_','-')+'.exe')),('./cmd/tools/'+$dcTool)) (Join-Path $paths.Cache ('logs/'+$dcTool+'-build.log'))
        }
        if($Runtime -eq 'v8'){Invoke-DcNative go @('build','-trimpath','-o',(Join-Path $dcRoot 'dc-control.exe'),'./cmd/server') (Join-Path $paths.Cache 'logs/v8-lan-control-build.log')}
        $dcReceiptArguments=@((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'complete',$dcRoot)
        if($VerifiedBuildEvidence){$dcReceiptArguments+=@('--parent',(Join-Path $paths.Cache 'builds/v8-p5'))}
        Invoke-DcNative python $dcReceiptArguments (Join-Path $paths.Cache 'logs/v8-lan-build-receipt.log')
        $dcPackage=Join-Path $paths.Cache ('packages/'+$dcPackageStage+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
        [void][IO.Directory]::CreateDirectory($dcPackage)
        $dcClient=Join-Path $dcRoot 'DungeonContract'
        foreach($dcItem in Get-ChildItem -LiteralPath $dcClient){if($dcItem.Name -notlike '*DoNotShip*'){Copy-Item -LiteralPath $dcItem.FullName -Destination $dcPackage -Recurse}}
        if($P5){
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dc-v8-player.ps1') -Destination $dcPackage
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dc-native-arguments.ps1') -Destination $dcPackage
            Copy-Item -LiteralPath (Join-Path $paths.Project 'start-v8-player.cmd') -Destination $dcPackage
            Copy-Item -LiteralPath (Join-Path $paths.Project 'docs/game-demo/v8-player-guide.md') -Destination $dcPackage
            foreach($dcReport in @('v8-validation.json','v8-audit.md','v8-performance.md','v8-performance-results.json','v8-evidence-inventory.json','v8-acceptance-results.json')){
                $dcReportSource=Join-Path $paths.Project ('docs/game-demo/'+$dcReport)
                if(Test-Path -LiteralPath $dcReportSource -PathType Leaf){Copy-Item -LiteralPath $dcReportSource -Destination $dcPackage}
            }
            # A carrier cannot embed its own final manifest hash. Keep this snapshot
            # about this folder and leave the host's final carrier fingerprint outside.
            $dcValidationCopy=Join-Path $dcPackage 'v8-validation.json'
            $dcValidationSnapshot=if(Test-Path -LiteralPath $dcValidationCopy){[IO.File]::ReadAllText($dcValidationCopy)|ConvertFrom-Json}else{[pscustomobject]@{profile='dc_ruins.v8.p5.1';status='NEW_SOURCE_EXPORT_BUILD_NOT_GAMEPLAY_VALIDATED';human=$false;physical_cross_pc=$false}}
            $dcValidationSnapshot|Add-Member -Force -NotePropertyName portable_carrier -NotePropertyValue ([pscustomobject]@{profile='dc_ruins.v8.p5.1';status='THIS_CANDIDATE_FOLDER_INTEGRITY_LISTED_IN_PACKAGE_MANIFEST';manifest='package-manifest.json';self_manifest_hash_embedded=$false;network_two_run_acceptance='NOT_VALIDATED_BY_PACKAGING';human=$false;cross_pc=$false})
            [IO.File]::WriteAllText($dcValidationCopy,($dcValidationSnapshot|ConvertTo-Json -Depth 64),[Text.UTF8Encoding]::new($false))
            $dcPackageGuide=Join-Path $dcPackage 'v8-player-guide.md'
            $dcGuideText=[IO.File]::ReadAllText($dcPackageGuide)
            $dcGuideText=$dcGuideText.Replace('(radmin-human-multiplayer-guide.md)','(联机教程.md)')
            $dcGuideText=$dcGuideText -replace '(?m)^> \d{4}-\d{2}-\d{2}启动器维护包：[^\r\n]*$', '> 本文件所在目录为当前完整玩家包，启动start-v8-player.cmd；不要单独复制启动脚本。'
            [IO.File]::WriteAllText($dcPackageGuide,$dcGuideText)
            # Reports also refer to host-only contracts; a portable folder must not ship broken local links.
            foreach($dcReportName in @('v8-audit.md','v8-performance.md')) {
                $dcReportPath=Join-Path $dcPackage $dcReportName
                if(!(Test-Path -LiteralPath $dcReportPath -PathType Leaf)){continue}
                $dcReportText=[IO.File]::ReadAllText($dcReportPath)
                $dcReportText=[regex]::Replace($dcReportText,'\[([^\]]+)\]\((?!https?://|#)([^)]+)\)',{
                    param($match)
                    $dcLocalTarget=($match.Groups[2].Value -split '#',2)[0]
                    if(Test-Path -LiteralPath (Join-Path $dcPackage $dcLocalTarget)){$match.Value}
                    else{$match.Groups[1].Value+'（主机工程 docs/game-demo/'+$match.Groups[2].Value+'）'}
                })
                [IO.File]::WriteAllText($dcReportPath,$dcReportText)
            }
        } else {
            Copy-Item -LiteralPath (Join-Path $dcRoot 'dc-lan-player.exe') -Destination $dcPackage
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dc-lan-player.ps1') -Destination $dcPackage
            Copy-Item -LiteralPath (Join-Path $paths.Project $(if($Runtime -eq 'v8'){'start-v8-lan-player.cmd'}else{'start-p4-lan-player.cmd'})) -Destination $dcPackage
        }
        $dcGuide=[IO.File]::ReadAllText((Join-Path $paths.Project 'docs/game-demo/radmin-human-multiplayer-guide.md')).Replace('[LAN实施与验证](lan-human-implementation.md)','主机工程中的 docs/game-demo/lan-human-implementation.md').Replace('[2.2实际契约](p2-2-runtime-contract.md)','主机工程中的 docs/game-demo/p2-2-runtime-contract.md')
        if($P5){$dcGuide=[regex]::Replace($dcGuide,'\[([^\]]+)\]\((?!https?://|#)([^)]+)\)',{param($match)if($match.Groups[2].Value.StartsWith('templates/') -or $match.Groups[2].Value -eq 'v8-player-guide.md'){$match.Value}else{$match.Groups[1].Value+'（主机工程 docs/game-demo/'+$match.Groups[2].Value+'）'}})}
        [IO.File]::WriteAllText((Join-Path $dcPackage '联机教程.md'),$dcGuide)
        [void][IO.Directory]::CreateDirectory((Join-Path $dcPackage 'templates'))
        foreach($dcTemplate in @('lan-human-playtest-record.md','p5-human-playtest-record.md','v5-human-playtest-record.md','v8-human-playtest-record.md')){
            $dcTemplateSource=Join-Path $paths.Project ('docs/game-demo/templates/'+$dcTemplate)
            if(Test-Path -LiteralPath $dcTemplateSource -PathType Leaf){Copy-Item -LiteralPath $dcTemplateSource -Destination (Join-Path $dcPackage ('templates/'+$dcTemplate))}
        }
        $dcFiles=@(Get-ChildItem -LiteralPath $dcPackage -File -Recurse|ForEach-Object{[pscustomobject]@{path=$_.FullName.Substring($dcPackage.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
        [IO.File]::WriteAllText((Join-Path $dcPackage 'package-manifest.json'),([pscustomobject]@{profile=$(if($P5){'dc_ruins.v8.p5.1'}else{'dc_ruins.'+$Runtime+'.p4.1'});player_build_source=$dcBuildSource;verified_binary_evidence=$VerifiedBuildEvidence;human_verified=$false;cross_pc_verified=$false;files=$dcFiles}|ConvertTo-Json -Depth 6))
        [IO.File]::WriteAllText((Join-Path $dcRoot 'player-package.json'),([pscustomobject]@{path=$dcPackage}|ConvertTo-Json))
        Write-Host ('Portable player folder: '+$dcPackage)
        Write-Host 'Send only this complete folder. It contains no private accounts, server executable or database credentials.'
    } else {
        if(!$BindAddress){throw 'Supply -BindAddress with the host Radmin or LAN IPv4.'}
        $dcAddress=$null
        if(![Net.IPAddress]::TryParse($BindAddress,[ref]$dcAddress) -or $dcAddress.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or $dcAddress.ToString() -ne $BindAddress){throw 'Use a plain IPv4 address.'}
        $dcBytes=$dcAddress.GetAddressBytes()
        if(!($dcBytes[0] -in @(10,26,127) -or ($dcBytes[0] -eq 192 -and $dcBytes[1] -eq 168) -or ($dcBytes[0] -eq 172 -and $dcBytes[1] -ge 16 -and $dcBytes[1] -le 31))){throw 'Only explicit local, RFC1918 LAN or Radmin IPv4 is allowed.'}
        $dcLocalIPv4=@([Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()|ForEach-Object{$_.GetIPProperties().UnicastAddresses}|Where-Object {$_.Address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork}|ForEach-Object{$_.Address.ToString()})
        if($dcBytes[0] -ne 127 -and $BindAddress -notin $dcLocalIPv4){throw 'This IPv4 does not belong to this computer. Start Radmin or correct the address.'}
        if($Action -eq 'Check') {
            $dcRequest=[Net.HttpWebRequest]::Create('http://'+$BindAddress+':12880/ready');$dcRequest.Proxy=$null;$dcRequest.Timeout=5000;$dcRequest.ReadWriteTimeout=5000
            $dcResponse=$dcRequest.GetResponse()
            try {Write-Host ('Player gateway ready: HTTP '+[int]$dcResponse.StatusCode+'. TCP is reachable; this does not test UDP or human gameplay.')} finally {$dcResponse.Dispose()}
        } else {
            Assert-DcCompose
            Invoke-DcNative python @((Join-Path $PSScriptRoot 'dc_v8_build_identity.py'),'verify',$dcRoot) (Join-Path $paths.Cache 'logs/v8-lan-host-build-check.log')
            if($AllowedPlayers.Count -lt 1 -or $AllowedPlayers.Count -gt 5){throw 'Supply exact allowed player IPv4s, including the host player address.'}
            if(@($AllowedPlayers|Select-Object -Unique).Count -ne $AllowedPlayers.Count){throw 'Allowed player IPv4 list contains duplicates.'}
            foreach($dcPeer in $AllowedPlayers){
                $dcPeerAddress=$null
                if(![Net.IPAddress]::TryParse($dcPeer,[ref]$dcPeerAddress) -or $dcPeerAddress.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or $dcPeerAddress.ToString() -ne $dcPeer){throw 'Every allowed player must be an exact IPv4 address.'}
                $dcPeerBytes=$dcPeerAddress.GetAddressBytes()
                if(!($dcPeerBytes[0] -in @(10,26,127) -or ($dcPeerBytes[0] -eq 192 -and $dcPeerBytes[1] -eq 168) -or ($dcPeerBytes[0] -eq 172 -and $dcPeerBytes[1] -ge 16 -and $dcPeerBytes[1] -le 31))){throw 'Player IPv4 must be local, RFC1918 LAN or Radmin.'}
            }
            $dcNetwork=[Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties()
            foreach($dcListener in $dcNetwork.GetActiveTcpListeners()){if($dcListener.Port -in @(12880,12890)){throw 'Another control/gateway listener is active.'}}
            foreach($dcListener in $dcNetwork.GetActiveUdpListeners()){if($dcListener.Port -eq 12777){throw 'Another battle UDP listener is active.'}}
            $dcControl=if($Runtime -eq 'v8'){Join-Path $dcRoot 'dc-control.exe'}else{Join-Path $paths.Cache 'builds/dc-control.exe'}
            $dcService=Join-Path $dcRoot 'GameService/GameService.exe'
            $dcGateway=Join-Path $dcRoot 'dc-lan-gateway.exe'
            foreach($dcExecutable in @($dcControl,$dcService,$dcGateway)){if(!(Test-Path -LiteralPath $dcExecutable)){throw 'Build control and LAN roles first.'}}
            $dcLogs=Join-Path $paths.Cache ('evidence/'+$dcPackageStage+'-host-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
            [void][IO.Directory]::CreateDirectory($dcLogs)
            $dcProcess=Start-Process -FilePath $dcControl -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $dcLogs 'control.log') -RedirectStandardError (Join-Path $dcLogs 'control-error.log')
            $dcProcesses.Add($dcProcess)
            $dcReady=$false
            for($dcAttempt=0;$dcAttempt -lt 50;$dcAttempt++){if($dcProcess.HasExited){throw 'Control failed; inspect its log.'};try{$null=Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:12880/ready' -TimeoutSec 1;$dcReady=$true;break}catch{Start-Sleep -Milliseconds 200}}
            if(!$dcReady){throw 'Local control did not become ready.'}
            foreach($entry in @(Get-ChildItem Env:|Where-Object Name -Match 'PASSWORD|SECRET|TOKEN|API_KEY|^DC_ADMISSION_KEY$')){if(!$dcOriginal.ContainsKey($entry.Name)){$dcOriginal[$entry.Name]=$entry.Value};[Environment]::SetEnvironmentVariable($entry.Name,$null,'Process')}
            $dcServiceArgs=@('-job-worker-count','2','-dc-role','server','-dc-runtime',$Runtime,'-dc-stage','p4','-dc-battle','true','-dc-listen-host',$BindAddress,'-dc-config',$env:DC_CONFIG_PATH,'-dc-service-credential',(Join-Path $paths.Private 'service-token.txt'),'-batchmode','-nographics','-logFile',(Join-Path $dcLogs 'service.log'))
            if($P5){$dcServiceArgs+=@('-dc-p5','true')}
            $dcProcesses.Add((Start-Process -FilePath $dcService -ArgumentList (Get-DcNativeArgumentString $dcServiceArgs) -WindowStyle Hidden -PassThru))
            $dcProcesses.Add((Start-Process -FilePath $dcGateway -ArgumentList (Get-DcNativeArgumentString @('-host',$BindAddress,'-allow',($AllowedPlayers -join ','))) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $dcLogs 'gateway.log') -RedirectStandardError (Join-Path $dcLogs 'gateway-error.log')))
            $dcP5Player=$null
            if($LocalPlayer){$dcP5Player=Start-Process -FilePath (Join-Path $dcRoot 'DungeonContract/DungeonContract.exe') -ArgumentList (Get-DcNativeArgumentString @('-job-worker-count','2','-dc-runtime','v8','-dc-p5','true','-dc-server-host',$BindAddress,'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',(Join-Path $dcLogs 'local-player.log'))) -WindowStyle Normal -PassThru;$dcProcesses.Add($dcP5Player)}
            Write-Host ('Host started on '+$BindAddress+' TCP12880 / UDP12777. Logs: '+$dcLogs)
            Write-Host 'Keep this window open. Start each player from the portable folder within 30 seconds of one another.'
            Write-Host 'After everyone closes the player, press Q here to stop owned Demo processes. This does not change VPN, firewall or Docker volumes.'
            $dcStopAt=if($RunSeconds -gt 0){[DateTime]::UtcNow.AddSeconds($RunSeconds)}else{[DateTime]::MaxValue}
            while($true){
                if($LocalPlayer -and $dcP5Player.HasExited){break}
                foreach($dcRunning in $dcProcesses){if($dcRunning.HasExited){throw 'A host process exited; inspect the displayed log folder.'}}
                if([DateTime]::UtcNow -ge $dcStopAt){break}
                if(![Console]::IsInputRedirected -and [Console]::KeyAvailable -and [Console]::ReadKey($true).Key -eq 'Q'){break}
                Start-Sleep -Milliseconds 300
            }
        }
    }
} finally {
    for($dcIndex=$dcProcesses.Count-1;$dcIndex -ge 0;$dcIndex--){$dcOwned=$dcProcesses[$dcIndex];if(!$dcOwned.HasExited){Stop-Process -InputObject $dcOwned -ErrorAction SilentlyContinue;$null=$dcOwned.WaitForExit(5000)}}
    Set-Location $dcLocation
    foreach($entry in $dcOriginal.GetEnumerator()){[Environment]::SetEnvironmentVariable($entry.Key,$entry.Value,'Process')}
    if($dcLocked){$dcLock.ReleaseMutex()};$dcLock.Dispose()
}
