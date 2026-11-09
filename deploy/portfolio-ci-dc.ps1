param(
    [switch]$Race,
    [string]$ReportPath=''
)

Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compose=Join-Path $PSScriptRoot 'portfolio-ci-dc-compose.yml'

function New-PortfolioSecret {
    [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
}

function Invoke-PortfolioDocker {
    param([string[]]$Arguments)
    & docker @Arguments 2>&1 | ForEach-Object { Protect-PortfolioDiagnosticText $_ }
    $report.command_exit_code=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){throw "Docker command failed (exit $LASTEXITCODE)."}
}

function Protect-PortfolioDiagnosticText {
    param([AllowNull()][object]$Value,[int]$MaximumCharacters=4096)
    $text=[string]$Value
    foreach($secret in $diagnosticSecrets){
        $text=$text.Replace($secret,'[REDACTED]').Replace($secret.ToUpperInvariant(),'[REDACTED]')
    }
    # Replace complete credentials before applying a bound, so truncation cannot
    # leave the beginning of a credential in a saved diagnostic.
    if($text.Length -gt $MaximumCharacters){$text=$text.Substring(0,$MaximumCharacters)+' [TRUNCATED]'}
    $text
}

function Read-PortfolioDiagnosticDocker {
    param([string[]]$Arguments,[int]$MaximumLines=80,[int]$MaximumCharacters=32768)
    $lines=[Collections.Generic.List[string]]::new()
    $characters=0;$truncated=$false
    & docker @Arguments 2>&1 | ForEach-Object {
        $safe=Protect-PortfolioDiagnosticText $_ -MaximumCharacters $MaximumCharacters
        $remaining=$MaximumCharacters-$characters
        if($lines.Count -lt $MaximumLines -and $remaining -gt 0){
            if($safe.Length -gt $remaining){$safe=$safe.Substring(0,$remaining);$truncated=$true}
            $lines.Add($safe);$characters+=$safe.Length
        }else{$truncated=$true}
    }
    [pscustomobject]@{exit_code=$LASTEXITCODE;lines=$lines.ToArray();truncated=$truncated}
}

function Get-PortfolioFailureDiagnostics {
    $diagnostics=[ordered]@{phase=$report.phase;operation=$report.operation;command_exit_code=$report.command_exit_code;captured_before_cleanup=$true;containers=@();errors=@()}
    $knownServices=@('mysql','redis','runner')
    # Obtain IDs from this exact random project only. Inspect only selected state
    # and labels, never Config.Env, Args or the health-check command definition.
    $lookup=Read-PortfolioDiagnosticDocker @('ps','--all','--filter',('label=com.docker.compose.project='+$composeProject),'--format','{{.ID}}') -MaximumLines 4 -MaximumCharacters 512
    if($lookup.exit_code -ne 0){
        $diagnostics.errors+=@('Project container lookup failed (exit '+$lookup.exit_code+').')
        return [pscustomobject]$diagnostics
    }
    $format='{"project":{{json (index .Config.Labels "com.docker.compose.project")}},"service":{{json (index .Config.Labels "com.docker.compose.service")}},"status":{{json .State.Status}},"running":{{json .State.Running}},"exit_code":{{json .State.ExitCode}},"oom_killed":{{json .State.OOMKilled}},"error":{{json .State.Error}},"health":{{if .State.Health}}{{json .State.Health}}{{else}}null{{end}}}'
    $ids=@($lookup.lines|Select-Object -Unique)
    if($lookup.truncated -or $ids.Count -gt 3){$diagnostics.errors+=@('Project container listing exceeded the three-service diagnostic bound.')}
    foreach($id in @($ids|Select-Object -First 3)){
        if($id -notmatch '^[a-f0-9]{12,64}$'){$diagnostics.errors+=@('Invalid project container ID was ignored.');continue}
        try{
            $snapshot=Read-PortfolioDiagnosticDocker @('inspect','--type','container','--format',$format,$id) -MaximumLines 1 -MaximumCharacters 65536
            if($snapshot.exit_code -ne 0 -or $snapshot.truncated){throw 'Selected container state could not be read within the diagnostic bound.'}
            $state=($snapshot.lines -join [Environment]::NewLine)|ConvertFrom-Json
            if($state.project -ne $composeProject -or $state.service -notin $knownServices){throw 'Container labels do not match this isolated project and service; logs were not read.'}
            $health=[ordered]@{status='NOT_CONFIGURED';failing_streak=0;checks=@()}
            if($null -ne $state.health){
                $health.status=Protect-PortfolioDiagnosticText $state.health.Status
                $health.failing_streak=$state.health.FailingStreak
                foreach($check in @($state.health.Log|Select-Object -Last 5)){
                    $health.checks+=@([pscustomobject]@{started_at=$check.Start;completed_at=$check.End;exit_code=$check.ExitCode;output=(Protect-PortfolioDiagnosticText $check.Output)})
                }
            }
            $container=[ordered]@{service=$state.service;container_id=$id;status=$state.status;running=$state.running;exit_code=$state.exit_code;oom_killed=$state.oom_killed;error=(Protect-PortfolioDiagnosticText $state.error);health=[pscustomobject]$health;log_exit_code=$null;log_truncated=$false;log_tail=@()}
            # Keep health information even if a subsequent log request fails.
            try{
                $logs=Read-PortfolioDiagnosticDocker @('logs','--tail','40','--timestamps',$id) -MaximumLines 40 -MaximumCharacters 16384
                $container.log_exit_code=$logs.exit_code;$container.log_truncated=$logs.truncated;$container.log_tail=$logs.lines
            }catch{$container.log_tail=@('Log capture failed: '+(Protect-PortfolioDiagnosticText $_.Exception.Message))}
            $diagnostics.containers+=@([pscustomobject]$container)
        }catch{$diagnostics.errors+=@((Protect-PortfolioDiagnosticText $_.Exception.Message))}
    }
    foreach($service in $knownServices){
        if($service -notin @($diagnostics.containers|ForEach-Object service)){
            $diagnostics.containers+=@([pscustomobject]@{service=$service;status='NOT_OBSERVED';health=$null})
        }
    }
    [pscustomobject]$diagnostics
}

function Set-PortfolioTemporaryPrivacy {
    param([string]$Path)
    if($IsWindows){
        $info=[IO.DirectoryInfo]::new($Path)
        $acl=[IO.FileSystemAclExtensions]::GetAccessControl($info,[Security.AccessControl.AccessControlSections]::Access)
        $acl.SetAccessRuleProtection($true,$false)
        foreach($rule in @($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))){[void]$acl.RemoveAccessRuleAll($rule)}
        foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,[Security.Principal.SecurityIdentifier]::new('S-1-5-18'))){
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
        }
        [IO.FileSystemAclExtensions]::SetAccessControl($info,$acl)
    }else{
        & chmod 700 -- $Path
        if($LASTEXITCODE -ne 0){throw 'Unable to protect temporary CI credentials.'}
    }
}

$temporary=''
$composeProject='portfolio-dc-ci-'+[Guid]::NewGuid().ToString('N').Substring(0,10)
$composeArguments=@()
$started=$false
$failure=$null
$diagnosticSecrets=@()
$report=[ordered]@{schema_version=1;status='NOT_RUN';phase='PREFLIGHT';operation='PREFLIGHT';command_exit_code=$null;suite='current_v8_isolated_storage';race=[bool]$Race;project=$composeProject;started_at=[DateTimeOffset]::UtcNow.ToString('o');passed_tests=@();skipped_tests=@();failures=@();diagnostics=$null;cleanup='NOT_NEEDED'}
try{
    if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 is required.'}
    if(!(Test-Path -LiteralPath $compose -PathType Leaf)){throw 'Portfolio DC compose file is missing.'}
    if(!(Get-Command docker -ErrorAction SilentlyContinue)){throw 'ENV_DOCKER_BLOCKED: Docker is required for isolated DC integration.'}
    if($IsWindows){
        try{$memory=Get-CimInstance Win32_OperatingSystem}catch{throw 'ENV_RESOURCE_BLOCKED: memory availability could not be checked; no containers started.'}
        if([UInt64]$memory.FreeVirtualMemory*1KB -lt 3328MB -or [UInt64]$memory.FreePhysicalMemory*1KB -lt 2304MB){
            throw 'ENV_RESOURCE_BLOCKED: isolated integration requires 3.25 GiB available commit and 2.25 GiB physical memory; no containers started.'
        }
    }
    try{& docker version --format '{{.Server.Version}}' | Out-Null}catch{throw 'ENV_DOCKER_BLOCKED: Docker engine is unavailable; no containers started.'}
    if($LASTEXITCODE -ne 0){throw 'ENV_DOCKER_BLOCKED: Docker engine is unavailable; no containers started.'}
    $report.phase='CONFIGURATION'
    $temporary=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('dc-portfolio-ci-'+[Guid]::NewGuid().ToString('N'))))
    [void][IO.Directory]::CreateDirectory($temporary)
    $envFile=Join-Path $temporary 'compose.env'
    $composeArguments=@('compose','--project-name',$composeProject,'--env-file',$envFile,'--file',$compose)
    Set-PortfolioTemporaryPrivacy $temporary
    if($project -match '[\r\n]'){throw 'Project path contains a newline.'}
    $values=[ordered]@{
        PORTFOLIO_PROJECT_ROOT=$project.Replace('\','/')
        PORTFOLIO_CI_ROOT_PASSWORD=New-PortfolioSecret
        PORTFOLIO_CI_DB_PASSWORD=New-PortfolioSecret
        PORTFOLIO_CI_REDIS_PASSWORD=New-PortfolioSecret
        PORTFOLIO_CI_JWT_SECRET=New-PortfolioSecret
        PORTFOLIO_CI_ADMISSION_KEY=New-PortfolioSecret
    }
    $diagnosticSecrets=@($values.GetEnumerator()|Where-Object Key -NE 'PORTFOLIO_PROJECT_ROOT'|ForEach-Object Value)
    [IO.File]::WriteAllLines($envFile,@($values.GetEnumerator()|ForEach-Object{'{0}={1}' -f $_.Key,$_.Value}),[Text.UTF8Encoding]::new($false))
    $report.operation='COMPOSE_CONFIGURATION'
    $json=@(& docker @composeArguments config --format json 2>&1)
    $report.command_exit_code=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){throw 'Isolated Compose configuration is invalid.'}
    $configuration=$json|ConvertFrom-Json
    if($configuration.name -ne $composeProject -or @($configuration.services.PSObject.Properties).Count -ne 3){throw 'Unexpected isolated Compose project.'}
    foreach($service in $configuration.services.PSObject.Properties){
        if($service.Value.PSObject.Properties.Name -contains 'ports'){throw 'Isolated integration must not publish host ports.'}
        if($service.Value.PSObject.Properties.Name -contains 'container_name'){throw 'Isolated integration must not reuse a container name.'}
    }
    foreach($volume in $configuration.volumes.PSObject.Properties){
        if($volume.Value.name -ne ($composeProject+'_'+$volume.Name)){throw 'Isolated integration attempted to reuse an external volume.'}
    }
    $started=$true
    $report.phase='INTEGRATION'
    $report.operation='COMPOSE_UP'
    Invoke-PortfolioDocker ($composeArguments+@('up','--detach','--wait','--wait-timeout','180'))
    foreach($stage in @('demo','v3-p0','v4-sync','v4-p3')){
        $report.operation='MIGRATE_'+$stage
        $confirmation=if($stage -eq 'demo'){'V2_MIGRATION_CONFIRM=I_UNDERSTAND_V2_MIGRATION'}else{'DC_V3_P0_CONFIRM=I_UNDERSTAND_DC_V3_P0_MIGRATION'}
        Invoke-PortfolioDocker ($composeArguments+@('exec','-T','runner','timeout','180s','env',$confirmation,'go','run','./cmd/tools/v2_migrate','-stage',$stage))
    }
    $testArguments=@('exec','-T','runner','timeout','600s','go','test','-p','1','-count=1','-timeout','300s','-run','^TestV8','-json')
    if($Race){$testArguments+='-race'}
    $testArguments+='./internal/pve/gameplay'
    $report.operation='GO_TEST'
    $output=@(& docker @composeArguments @testArguments 2>&1)
    $testExit=$LASTEXITCODE
    $report.command_exit_code=$testExit
    $events=@($output|ForEach-Object{try{ConvertFrom-Json -InputObject ([string]$_) -ErrorAction Stop}catch{}})
    $report.passed_tests=@($events|Where-Object{$_.Action -eq 'pass' -and $_.PSObject.Properties.Name -contains 'Test'}|ForEach-Object Test)
    $report.skipped_tests=@($events|Where-Object{$_.Action -eq 'skip' -and $_.PSObject.Properties.Name -contains 'Test'}|ForEach-Object Test)
    $report.failures=@($events|Where-Object{$_.Action -eq 'fail'}|ForEach-Object{if($_.PSObject.Properties.Name -contains 'Test'){$_.Test}else{$_.Package}})
    $output|ForEach-Object {Protect-PortfolioDiagnosticText $_}|Write-Output
    if($testExit -ne 0 -or $report.failures.Count -gt 0){throw 'Current v8 isolated integration failed; review the emitted test output.'}
    if($report.skipped_tests.Count -gt 0){throw 'Current v8 isolated integration unexpectedly skipped tests.'}
    foreach($required in @('TestV8StorageAdmissionAndReliableIdempotency','TestV8StorageRejectsSummonAndOldBattleProtocol','TestV8StorageTerminalRewardDeduplicates','TestV8WorkerDispatchAndAttemptFencing')){
        if($required -notin $report.passed_tests){throw "Required storage test did not execute: $required"}
    }
    $report.status='PASS'
    $report.phase='COMPLETE'
}catch{
    $failure=Protect-PortfolioDiagnosticText $_.Exception.Message
    $report.status=if($_.Exception.Message.StartsWith('ENV_RESOURCE_BLOCKED:')){'ENV_RESOURCE_BLOCKED'}elseif($_.Exception.Message.StartsWith('ENV_DOCKER_BLOCKED:')){'ENV_DOCKER_BLOCKED'}else{'FAIL'}
    $report.failures+=@($failure)
    if($started){
        try{$report.diagnostics=Get-PortfolioFailureDiagnostics}catch{
            $report.diagnostics=[pscustomobject]@{captured_before_cleanup=$true;errors=@('Diagnostic capture failed: '+(Protect-PortfolioDiagnosticText $_.Exception.Message))}
        }
    }
}finally{
    if($started){
        try{
            & docker @composeArguments down --volumes --remove-orphans 2>&1 | ForEach-Object {Protect-PortfolioDiagnosticText $_}|Out-Host
            $cleanupExit=$LASTEXITCODE
            $report.cleanup=if($cleanupExit -eq 0){'PASS'}else{'FAIL'}
            if($cleanupExit -ne 0){$report.status='FAIL';$report.failures+=@('Isolated Compose cleanup failed (exit '+$cleanupExit+').')}
        }catch{$report.cleanup='FAIL';$report.status='FAIL';$report.failures+=@('Isolated Compose cleanup failed: '+(Protect-PortfolioDiagnosticText $_.Exception.Message))}
    }
    if($temporary){
        $tempParent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
        if(!$temporary.StartsWith($tempParent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($temporary) -notmatch '^dc-portfolio-ci-[0-9a-f]{32}$'){throw 'Temporary cleanup boundary mismatch.'}
        if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Recurse -Force}
    }
    $report.completed_at=[DateTimeOffset]::UtcNow.ToString('o')
    $reportJson=Protect-PortfolioDiagnosticText ($report|ConvertTo-Json -Depth 8) -MaximumCharacters ([int]::MaxValue)
    if($ReportPath){[IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportPath),$reportJson+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))}
    $reportJson|Write-Output
}
if($failure){throw $failure}
if($report.status -ne 'PASS'){throw 'Isolated integration did not complete successfully.'}
