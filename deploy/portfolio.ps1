#requires -Version 7.2
param(
    [Parameter(Mandatory)]
    [ValidateSet('Prepare','PrepareUnity','Validate','Initialize','ServicesStart','ServicesStop','Status','CheckServices','GoTest','GoVet','GoBuild','Server')]
    [string]$Action,
    [ValidateSet('v2','v8')][string]$Runtime='v8',
    [string]$ProjectRoot='',
    [string]$CacheRoot='',
    [string]$PrivateRoot=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$dcOriginalEnv=@{}
$dcConfigured=@{}
foreach($dcBinding in @{DC_PROJECT_ROOT=$ProjectRoot;DC_CACHE_ROOT=$CacheRoot;DC_PRIVATE_ROOT=$PrivateRoot}.GetEnumerator()){
    if($dcBinding.Value){$dcConfigured[$dcBinding.Key]=$dcBinding.Value}
}
function Set-PortfolioVariable([string]$Name,$Value){
    if(!$dcOriginalEnv.ContainsKey($Name)){$dcOriginalEnv[$Name]=[Environment]::GetEnvironmentVariable($Name,'Process')}
    [Environment]::SetEnvironmentVariable($Name,$Value,'Process')
}
try {
    foreach($dcEntry in $dcConfigured.GetEnumerator()){Set-PortfolioVariable $dcEntry.Key $dcEntry.Value}
    . (Join-Path $PSScriptRoot 'dc-common.ps1')
    $dcPaths=Get-DcPaths
    function Initialize-PortfolioCache {
        foreach($dcRelative in @('','go-build','go-mod','tmp','builds','logs','evidence')){
            $dcDirectory=if($dcRelative){Join-Path $dcPaths.Cache $dcRelative}else{$dcPaths.Cache}
            if(!(Test-Path -LiteralPath $dcDirectory -PathType Container)){[void][IO.Directory]::CreateDirectory($dcDirectory)}
            if(((Get-Item -LiteralPath $dcDirectory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Cache directories cannot be reparse points.'}
        }
    }
    function Assert-PortfolioPrivatePermissions {
        $dcInfo=[IO.DirectoryInfo]::new($dcPaths.Private)
        if(($dcInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'The private directory cannot be a reparse point.'}
        $dcAcl=[IO.FileSystemAclExtensions]::GetAccessControl($dcInfo,[Security.AccessControl.AccessControlSections]::Access)
        if(!$dcAcl.AreAccessRulesProtected){throw 'Private directory permissions must be prepared before reading secrets.'}
        $dcOwner=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $dcUserAllowed=$false
        foreach($dcRule in $dcAcl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
            if($dcRule.AccessControlType -eq 'Allow' -and $dcRule.IdentityReference.Value -notin @($dcOwner,'S-1-5-18')){throw 'The private directory permits an unexpected identity.'}
            if($dcRule.AccessControlType -eq 'Allow' -and $dcRule.IdentityReference.Value -eq $dcOwner){$dcUserAllowed=$true}
        }
        if(!$dcUserAllowed){throw 'The current user has no explicit access to private storage.'}
        foreach($dcFile in $dcInfo.GetFiles()){
            if(($dcFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Private files cannot be reparse points.'}
            $dcFileAcl=[IO.FileSystemAclExtensions]::GetAccessControl($dcFile,[Security.AccessControl.AccessControlSections]::Access)
            foreach($dcRule in $dcFileAcl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
                if($dcRule.AccessControlType -eq 'Allow' -and $dcRule.IdentityReference.Value -notin @($dcOwner,'S-1-5-18')){throw 'A private file permits an unexpected identity.'}
            }
        }
    }
    function Invoke-PortfolioNative([string]$Executable,[string[]]$Arguments,[string]$LogName=''){
        if($LogName){
            $dcLog=Join-Path $dcPaths.Cache ('logs/portfolio-'+$LogName+'.log')
            & $Executable @Arguments 2>&1 | Tee-Object -FilePath $dcLog
        }else{& $Executable @Arguments}
        if($LASTEXITCODE -ne 0){throw "Command failed: $Executable (exit $LASTEXITCODE)."}
    }
    function Set-PortfolioEnvironment([switch]$IncludeCredentials){
        foreach($dcEntry in (Get-DcEnvironment -IncludeCredentials:$IncludeCredentials -Runtime $Runtime).GetEnumerator()){
            Set-PortfolioVariable $dcEntry.Key $dcEntry.Value
        }
        foreach($dcFlag in @('DC_V8_TEST','DC_ENGINEERING_RUN','DC_QUALITY_TEST','DC_P5_TRIAL_TEST','DC_V3_P0_TEST','DC_V3_P1_TEST','DC_S1_TEST','DC_S2_TEST','DC_P2_MP_TEST','DC_P3_TEST','DC_P4_TEST','DC_V5_TEST','DC_P5_TEST','DC_V6_TEST','DC_V7_TEST')){Set-PortfolioVariable $dcFlag $null}
        foreach($dcSecret in @('DB_PASSWORD','REDIS_PASSWORD','JWT_SECRET','DC_SERVICE_TOKEN','DC_ADMISSION_KEY','PVE_METRICS_TOKEN')){
            if(!$IncludeCredentials){Set-PortfolioVariable $dcSecret $null}
        }
    }
    function Assert-PortfolioPrivate {
        if(!(Test-Path -LiteralPath $dcPaths.Private -PathType Container)){throw 'Run Prepare before accessing backend credentials.'}
        Assert-PortfolioPrivatePermissions
        Assert-DcBackendLayout
    }
    function Prepare-PortfolioPrivate {
        $dcRequired=@('root-password.txt','db-password.txt','redis-password.txt','jwt-secret.txt','service-token.txt','admission-key.txt','metrics-token.txt')
        if(!(Test-Path -LiteralPath $dcPaths.Private -PathType Container)){[void][IO.Directory]::CreateDirectory($dcPaths.Private)}
        $dcInfo=[IO.DirectoryInfo]::new($dcPaths.Private)
        if(($dcInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'The private directory cannot be a reparse point.'}
        $dcEntries=@($dcInfo.GetFileSystemInfos())
        $dcPresent=@($dcRequired|Where-Object{Test-Path -LiteralPath (Join-Path $dcPaths.Private $_) -PathType Leaf})
        if($dcPresent.Count -gt 0 -and $dcPresent.Count -ne $dcRequired.Count){throw 'Partial credentials detected; existing passwords are never rotated automatically.'}
        if($dcPresent.Count -eq 0){
            if($dcEntries.Count){throw 'Unrecognized private directory contents; no credentials were created.'}
            # If Docker is available, inspect the existing namespace before making
            # new secrets. An unavailable engine must not create replacement keys.
            if(Get-Command docker -ErrorAction SilentlyContinue){Assert-PortfolioStorageOwner -RequireEmpty}
            $dcAcl=[IO.FileSystemAclExtensions]::GetAccessControl($dcInfo,[Security.AccessControl.AccessControlSections]::Access)
            $dcAcl.SetAccessRuleProtection($true,$false)
            foreach($dcRule in @($dcAcl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))){[void]$dcAcl.RemoveAccessRuleAll($dcRule)}
            foreach($dcSid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'))){
                $dcAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($dcSid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
            }
            [IO.FileSystemAclExtensions]::SetAccessControl($dcInfo,$dcAcl)
            foreach($dcName in $dcRequired){
                $dcSecret=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
                $dcStream=[IO.File]::Open((Join-Path $dcPaths.Private $dcName),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
                try{$dcBytes=[Text.Encoding]::UTF8.GetBytes($dcSecret);$dcStream.Write($dcBytes,0,$dcBytes.Length)}finally{$dcStream.Dispose()}
            }
        }
        Assert-PortfolioPrivatePermissions
        $dcRedisSecret=[IO.File]::ReadAllText((Join-Path $dcPaths.Private 'redis-password.txt')).Trim()
        $dcRedisText="bind 0.0.0.0`nprotected-mode yes`nport 6379`ndir /data`nappendonly yes`nrequirepass $dcRedisSecret`n"
        $dcRedisPath=Join-Path $dcPaths.Private 'redis.conf'
        if(Test-Path -LiteralPath $dcRedisPath){
            if([IO.File]::ReadAllText($dcRedisPath) -ne $dcRedisText){throw 'Existing Redis settings differ; no file was overwritten.'}
        }else{
            $dcStream=[IO.File]::Open($dcRedisPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try{$dcBytes=[Text.Encoding]::UTF8.GetBytes($dcRedisText);$dcStream.Write($dcBytes,0,$dcBytes.Length)}finally{$dcStream.Dispose()}
        }
        Assert-PortfolioPrivate
    }
    function Assert-PortfolioStorageOwner([switch]$RequireEmpty) {
        $dcIds=@(& docker ps -a --filter 'label=com.docker.compose.project=dc-demo' --format '{{.ID}}')
        if($LASTEXITCODE -ne 0){throw 'Docker storage identity could not be checked; no storage was changed.'}
        if($dcIds.Count){
            if($RequireEmpty){throw 'Existing dc-demo containers require their original credentials; no credentials were created.'}
            $dcContainers=@(& docker inspect @dcIds | ConvertFrom-Json)
            if($LASTEXITCODE -ne 0){throw 'Existing container identity could not be checked.'}
            foreach($dcContainer in $dcContainers){
                foreach($dcMount in @($dcContainer.Mounts|Where-Object{$_.Destination -like '/run/secrets/*'})){
                    $dcSource=[string]$dcMount.Source
                    $dcExpected=$dcPaths.Private.TrimEnd('\')+'\'
                    if($dcSource -match '^/run/desktop/mnt/host/([a-z])/(.*)$'){$dcSource=$matches[1]+':\'+$matches[2].Replace('/','\')}
                    if(!$dcSource.StartsWith($dcExpected,[StringComparison]::OrdinalIgnoreCase)){throw 'Existing dc-demo containers use another private directory; no storage was changed.'}
                    $dcSecretName=[IO.Path]::GetFileName($dcSource)
                    $dcKnownNames=@('root-password.txt','db-password.txt','redis-password.txt','redis.conf')
                    if($dcSecretName -notin $dcKnownNames -or [IO.Path]::GetFullPath($dcSource) -ne (Join-Path $dcPaths.Private $dcSecretName)){throw 'Unexpected existing secret file binding.'}
                }
                if(@($dcContainer.Mounts|Where-Object{$_.Destination -like '/run/secrets/*'}).Count -eq 0){throw 'Existing dc-demo container has no expected secret mounts.'}
            }
        }else{
            $dcVolumes=@(& docker volume ls --filter 'label=com.docker.compose.project=dc-demo' --format '{{.Name}}')
            if($LASTEXITCODE -ne 0){throw 'Docker volume identity could not be checked.'}
            if($dcVolumes.Count){throw 'Existing dc-demo volumes have no matching container credentials; recover them explicitly before starting.'}
        }
    }
    Initialize-PortfolioCache
    if($Action -in @('Prepare','PrepareUnity')){
        Prepare-PortfolioPrivate
        if($Action -eq 'PrepareUnity'){& (Join-Path $PSScriptRoot 'dc-prepare.ps1')}
        Write-Output 'Portable directories and private credentials are ready; existing credentials were preserved.'
        return
    }
    $dcNeedsCredentials=$Action -notin @('GoTest','GoVet','GoBuild')
    if($dcNeedsCredentials){Assert-PortfolioPrivate}
    Set-PortfolioEnvironment -IncludeCredentials:$dcNeedsCredentials
    if($Action -in @('Initialize','ServicesStart','ServicesStop','Status','CheckServices','Server')){Assert-PortfolioStorageOwner}
    switch($Action){
        'Validate'{Assert-DcCompose;Write-Output 'Portable paths, private permissions and loopback Compose isolation are valid.'}
        'ServicesStart'{Assert-DcCompose;Invoke-PortfolioNative docker ((Get-DcComposeArguments)+@('up','--detach','--pull','missing','--wait','--wait-timeout','180')) 'services-start'}
        'ServicesStop'{Assert-DcCompose;Invoke-PortfolioNative docker ((Get-DcComposeArguments)+@('stop','--timeout','30')) 'services-stop'}
        'Status'{Invoke-PortfolioNative docker ((Get-DcComposeArguments)+@('ps')) 'status'}
        'Initialize'{
            Push-Location (Join-Path $dcPaths.Project 'backend')
            try{
                foreach($dcStage in @('demo','v3-p0','v4-sync','v4-p3')){
                    Set-PortfolioVariable 'V2_MIGRATION_CONFIRM' 'I_UNDERSTAND_V2_MIGRATION'
                    Set-PortfolioVariable 'DC_V3_P0_CONFIRM' 'I_UNDERSTAND_DC_V3_P0_MIGRATION'
                    Invoke-PortfolioNative go @('run','./cmd/tools/v2_migrate','-stage',$dcStage) ('initialize-'+$dcStage)
                }
            }finally{Pop-Location}
        }
        'CheckServices'{Push-Location (Join-Path $dcPaths.Project 'backend');try{Invoke-PortfolioNative go @('run','./cmd/tools/dc_preflight') 'check-services'}finally{Pop-Location}}
        'GoTest'{Push-Location (Join-Path $dcPaths.Project 'backend');try{Invoke-PortfolioNative go @('test','-p','1','./...') 'go-test'}finally{Pop-Location}}
        'GoVet'{Push-Location (Join-Path $dcPaths.Project 'backend');try{Invoke-PortfolioNative go @('vet','-p','1','./...') 'go-vet';Invoke-PortfolioNative go @('mod','verify') 'go-mod-verify'}finally{Pop-Location}}
        'GoBuild'{Push-Location (Join-Path $dcPaths.Project 'backend');try{Invoke-PortfolioNative go @('build','-p','1','-trimpath','-o',(Join-Path $dcPaths.Cache 'builds/dc-control.exe'),'./cmd/server') 'go-build'}finally{Pop-Location}}
        'Server'{
            $dcBinary=Join-Path $dcPaths.Cache 'builds/dc-control.exe'
            if(!(Test-Path -LiteralPath $dcBinary -PathType Leaf)){throw 'Run GoBuild before Server.'}
            Push-Location (Join-Path $dcPaths.Project 'backend');try{Invoke-PortfolioNative $dcBinary @() 'server'}finally{Pop-Location}
        }
    }
}finally{
    foreach($dcEntry in $dcOriginalEnv.GetEnumerator()){[Environment]::SetEnvironmentVariable($dcEntry.Key,$dcEntry.Value,'Process')}
}
