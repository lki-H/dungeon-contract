#requires -Version 7.2
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$dcSource=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dcTestRoot=Join-Path ([IO.Path]::GetTempPath()) ('dungeon-portfolio-tests-'+[Guid]::NewGuid().ToString('N'))
$dcCases=[Collections.Generic.List[string]]::new()
$dcSaved=@{}
foreach($dcName in @('DC_PROJECT_ROOT','DC_CACHE_ROOT','DC_PRIVATE_ROOT','DC_UNITY_PATH','DB_PASSWORD','DC_V8_TEST','PVE_INTEGRATION')){
    $dcSaved[$dcName]=[Environment]::GetEnvironmentVariable($dcName,'Process')
    [Environment]::SetEnvironmentVariable($dcName,$null,'Process')
}
function Assert-PortfolioTest([bool]$Condition,[string]$Name){
    if(!$Condition){throw ('Assertion failed: '+$Name)}
    $dcCases.Add($Name)
}
function Expect-PortfolioFailure([scriptblock]$Operation,[string]$Name){
    $dcFailed=$false
    try{& $Operation | Out-Null}catch{$dcFailed=$true}
    Assert-PortfolioTest $dcFailed $Name
}
try{
    $dcRelocated=Join-Path $dcTestRoot 'source folder with spaces'
    foreach($dcPart in @('deploy','backend','docs/game-demo/config')){[void][IO.Directory]::CreateDirectory((Join-Path $dcRelocated $dcPart))}
    foreach($dcName in @('portfolio.ps1','dc-common.ps1','dc-native-arguments.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $dcName) -Destination (Join-Path $dcRelocated ('deploy/'+$dcName))}
    [IO.File]::WriteAllText((Join-Path $dcRelocated 'backend/go.mod'),'module portable-test')
    [IO.File]::WriteAllText((Join-Path $dcRelocated 'docs/game-demo/config/demo-v8-p5.json'),'{}')
    $dcCache=Join-Path $dcTestRoot 'cache with spaces'
    $dcPrivate=Join-Path $dcTestRoot 'private with spaces'
    $env:DC_PROJECT_ROOT=$dcRelocated
    $env:DC_CACHE_ROOT=$dcCache
    $env:DC_PRIVATE_ROOT=$dcPrivate
    . (Join-Path $dcRelocated 'deploy/dc-common.ps1')
    $dcPaths=Get-DcPaths
    Assert-PortfolioTest ($dcPaths.Project -eq $dcRelocated -and $dcPaths.Cache -eq $dcCache -and $dcPaths.Private -eq $dcPrivate) 'relocated source and spaced env paths'
    $dcEntry=Join-Path $dcRelocated 'deploy/portfolio.ps1'
    function global:docker {
        if($args[0] -eq 'ps' -or ($args[0] -eq 'volume' -and $args[1] -eq 'ls')){$global:LASTEXITCODE=0;return}
        throw 'Unexpected Docker command in a model test.'
    }
    & $dcEntry -Action Prepare | Out-Null
    $dcBefore=@{}
    foreach($dcFile in Get-ChildItem -LiteralPath $dcPrivate -File){$dcBefore[$dcFile.Name]=(Get-FileHash -LiteralPath $dcFile.FullName -Algorithm SHA256).Hash}
    Assert-PortfolioTest ($dcBefore.Count -eq 8) 'prepare creates seven secrets and Redis config'
    $dcAcl=[IO.FileSystemAclExtensions]::GetAccessControl([IO.DirectoryInfo]::new($dcPrivate),[Security.AccessControl.AccessControlSections]::Access)
    Assert-PortfolioTest $dcAcl.AreAccessRulesProtected 'private ACL disables inheritance'
    $dcSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $dcRules=@($dcAcl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
    Assert-PortfolioTest (@($dcRules|Where-Object{$_.AccessControlType -eq 'Allow' -and $_.IdentityReference.Value -notin @($dcSid,'S-1-5-18')}).Count -eq 0) 'private ACL permits only user and SYSTEM'
    & $dcEntry -Action Prepare | Out-Null
    foreach($dcFile in Get-ChildItem -LiteralPath $dcPrivate -File){Assert-PortfolioTest ($dcBefore[$dcFile.Name] -eq (Get-FileHash -LiteralPath $dcFile.FullName -Algorithm SHA256).Hash) ('idempotent prepare '+$dcFile.Name)}
    Assert-PortfolioTest ($env:DC_PROJECT_ROOT -eq $dcRelocated -and $env:DC_CACHE_ROOT -eq $dcCache -and $env:DC_PRIVATE_ROOT -eq $dcPrivate) 'caller environment preserved after prepare'
    $dcPublicFile=Join-Path $dcPrivate 'permissions-test.txt'
    [IO.File]::WriteAllText($dcPublicFile,'test fixture')
    try{
        $dcFileInfo=[IO.FileInfo]::new($dcPublicFile)
        $dcFileAcl=[IO.FileSystemAclExtensions]::GetAccessControl($dcFileInfo,[Security.AccessControl.AccessControlSections]::Access)
        $dcFileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-1-0'),'Read','Allow'))
        [IO.FileSystemAclExtensions]::SetAccessControl($dcFileInfo,$dcFileAcl)
        Expect-PortfolioFailure {& $dcEntry -Action Prepare} 'private file with unexpected read permissions rejected'
    }finally{Remove-Item -LiteralPath $dcPublicFile -Force}
    $dcOriginalLocation=Get-Location
    $dcFakeCalls=[Collections.Generic.List[object]]::new()
    function global:go {
        $dcFakeCalls.Add([pscustomobject]@{Arguments=@($args);Private=$env:DC_PRIVATE_ROOT;Password=$env:DB_PASSWORD;Flag=$env:DC_V8_TEST;Processors=$env:GOMAXPROCS;Location=(Get-Location).Path})
        $global:LASTEXITCODE=0
    }
    $env:DB_PASSWORD='caller-value-not-a-secret'
    $env:DC_V8_TEST='1'
    $dcUnusedPrivate=Join-Path $dcTestRoot 'unit tests need no private files'
    & $dcEntry -Action GoTest -PrivateRoot $dcUnusedPrivate | Out-Null
    Assert-PortfolioTest (!(Test-Path -LiteralPath $dcUnusedPrivate)) 'unit tests do not generate private files'
    Assert-PortfolioTest ($dcFakeCalls.Count -eq 1 -and !$dcFakeCalls[0].Password -and !$dcFakeCalls[0].Flag -and $dcFakeCalls[0].Processors -eq '2') 'unit tests clear secrets/integration flags and bound Go parallelism'
    Assert-PortfolioTest (($dcFakeCalls[0].Arguments -join ' ') -eq 'test -p 1 ./...') 'unit tests use serial package execution'
    Assert-PortfolioTest ($env:DB_PASSWORD -eq 'caller-value-not-a-secret' -and $env:DC_V8_TEST -eq '1' -and (Get-Location).Path -eq $dcOriginalLocation.Path) 'environment and working directory restored after unit tests'
    Remove-Item Function:global:go
    $dcMismatched=Join-Path $dcTestRoot 'second credentials'
    & $dcEntry -Action Prepare -PrivateRoot $dcMismatched | Out-Null
    function global:docker {
        if($args[0] -eq 'ps'){'test_original_container';$global:LASTEXITCODE=0;return}
        if($args[0] -eq 'inspect'){
            ConvertTo-Json -InputObject @([pscustomobject]@{Mounts=@([pscustomobject]@{Destination='/run/secrets/dc_db_password';Source=(Join-Path $dcPrivate 'db-password.txt')})}) -Depth 4
            $global:LASTEXITCODE=0
            return
        }
        throw 'Unexpected Docker mutation requested by negative test.'
    }
    Expect-PortfolioFailure {& $dcEntry -Action ServicesStart -PrivateRoot $dcMismatched} 'existing storage with another private root rejected before compose mutation'
    $dcReplacement=Join-Path $dcTestRoot 'replacement credentials'
    Expect-PortfolioFailure {& $dcEntry -Action Prepare -PrivateRoot $dcReplacement} 'existing containers refuse first credential creation'
    Assert-PortfolioTest (!(Test-Path -LiteralPath (Join-Path $dcReplacement 'db-password.txt'))) 'existing containers never receive replacement credentials'
    function global:docker {
        if($args[0] -eq 'ps'){$global:LASTEXITCODE=0;return}
        if($args[0] -eq 'volume'){'fixture-original-volume';$global:LASTEXITCODE=0;return}
        throw 'Unexpected Docker command in an existing-volume test.'
    }
    Expect-PortfolioFailure {& $dcEntry -Action Prepare -PrivateRoot $dcReplacement} 'orphaned existing volumes refuse first credential creation'
    function global:docker {$global:LASTEXITCODE=1}
    Expect-PortfolioFailure {& $dcEntry -Action Prepare -PrivateRoot $dcReplacement} 'offline engine refuses unchecked first credential creation'
    Assert-PortfolioTest (!(Test-Path -LiteralPath (Join-Path $dcReplacement 'db-password.txt'))) 'failed storage preflight creates no credentials'
    Remove-Item Function:global:docker
    $dcPartial=Join-Path $dcTestRoot 'partial credentials'
    [void][IO.Directory]::CreateDirectory($dcPartial)
    [IO.File]::WriteAllText((Join-Path $dcPartial 'root-password.txt'),'untouched')
    Expect-PortfolioFailure {& $dcEntry -Action Prepare -PrivateRoot $dcPartial} 'partial credentials rejected'
    Assert-PortfolioTest ([IO.File]::ReadAllText((Join-Path $dcPartial 'root-password.txt')) -eq 'untouched') 'partial credentials never overwritten'
    $dcUnknown=Join-Path $dcTestRoot 'unrecognized private data'
    [void][IO.Directory]::CreateDirectory($dcUnknown)
    [IO.File]::WriteAllText((Join-Path $dcUnknown 'readme.txt'),'untouched')
    Expect-PortfolioFailure {& $dcEntry -Action Prepare -PrivateRoot $dcUnknown} 'unknown private contents rejected'
    $dcMissingPrivate=Join-Path $dcTestRoot 'missing private'
    Expect-PortfolioFailure {& $dcEntry -Action Validate -PrivateRoot $dcMissingPrivate} 'validation refuses missing credentials'
    Assert-PortfolioTest (!(Test-Path -LiteralPath $dcMissingPrivate)) 'validation does not generate private files'
    Expect-PortfolioFailure {& $dcEntry -Action Prepare -PrivateRoot (Join-Path $dcRelocated 'private')} 'source-tree secrets rejected'
    Expect-PortfolioFailure {& $dcEntry -Action Prepare -CacheRoot (Join-Path $dcRelocated 'cache')} 'source-tree cache rejected'
    $dcLink=Join-Path $dcTestRoot 'linked private ancestor'
    New-Item -ItemType Junction -Path $dcLink -Target $dcRelocated | Out-Null
    try{Expect-PortfolioFailure {& $dcEntry -Action Prepare -PrivateRoot (Join-Path $dcLink 'private')} 'linked private ancestor rejected'}finally{Remove-Item -LiteralPath $dcLink -Force}
    Assert-PortfolioTest (!(Test-Path -LiteralPath (Join-Path $dcRelocated 'private'))) 'linked private path creates no source files'
    $env:DC_CACHE_ROOT=$null
    $env:DC_PRIVATE_ROOT=$null
    $dcDefault=Get-DcPaths
    Assert-PortfolioTest ($dcDefault.Cache -notlike 'D:\dev-cache\dungeon-contract*' -and $dcDefault.Private -ne $dcPrivate) 'fresh clone defaults outside developer cache'
    $dcPolicyHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($dcRelocated.ToLowerInvariant()))).Substring(0,12).ToLowerInvariant()
    Assert-PortfolioTest ($dcDefault.Cache -eq (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('DungeonContract/projects/'+$dcPolicyHash+'/cache'))) 'cross-language cache path policy SHA256 first12'
    $env:DC_PROJECT_ROOT=$dcSource
    $dcOther=Get-DcPaths
    Assert-PortfolioTest ($dcOther.Cache -ne $dcDefault.Cache) 'different source roots have independent defaults'
    [pscustomobject]@{status='PASS';cases=$dcCases.Count;checks=@($dcCases);docker_started=$false;secrets_printed=$false}|ConvertTo-Json -Depth 4
}finally{
    Remove-Item Function:global:go -ErrorAction SilentlyContinue
    Remove-Item Function:global:docker -ErrorAction SilentlyContinue
    foreach($dcEntry in $dcSaved.GetEnumerator()){[Environment]::SetEnvironmentVariable($dcEntry.Key,$dcEntry.Value,'Process')}
    if(Test-Path -LiteralPath $dcTestRoot){
        $dcResolved=[IO.Path]::GetFullPath($dcTestRoot)
        $dcTempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
        if(!$dcResolved.StartsWith($dcTempRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($dcResolved) -notlike 'dungeon-portfolio-tests-*'){throw 'Test cleanup boundary rejected.'}
        Remove-Item -LiteralPath $dcResolved -Recurse -Force
    }
}
