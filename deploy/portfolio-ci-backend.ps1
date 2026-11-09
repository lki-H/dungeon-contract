param(
    [switch]$Integration,
    [switch]$Race,
    [string]$ReportPath=''
)

Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 is required.'}
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$backend=Join-Path $project 'backend'
if(!(Test-Path -LiteralPath $backend -PathType Container)){throw 'Portfolio backend source is missing.'}

function Invoke-PortfolioCommand {
    param([string]$Executable,[string[]]$Arguments)
    & $Executable @Arguments
    if($LASTEXITCODE -ne 0){throw "Command failed: $Executable (exit $LASTEXITCODE)."}
}

$saved=@{}
$clearNames=@('PVE_INTEGRATION','PVE_TEST_NETWORK','PVE_TEST_PASSWORD','PVE_RULES_PATH','PVE_R4_SERVER_BINARY','PVE_R3_SERVER_BINARY','DC_INTEGRATION','DC_V3_P0_TEST','DC_V3_P1_TEST','DC_S1_TEST','DC_S2_TEST','DC_P2_MP_TEST','DC_P3_TEST','DC_P4_TEST','DC_V5_TEST','DC_P5_TEST','DC_V6_TEST','DC_V7_TEST','DC_V8_TEST','DC_P5_TRIAL_TEST','DC_QUALITY_TEST','DC_ENGINEERING_RUN','DC_CONFIG_PATH','DC_ADMISSION_KEY','DC_SERVICE_TOKEN','DC_V8_MANIFEST_MODEL_FILE','DC_GAME_ENABLED','GOMAXPROCS','GOFLAGS')
foreach($name in $clearNames){
    $saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
    [Environment]::SetEnvironmentVariable($name,$null,'Process')
}
$report=[ordered]@{schema_version=1;status='NOT_RUN';suite='backend_unit_vet_modules';started_at=[DateTimeOffset]::UtcNow.ToString('o');passed_tests=0;skipped_tests=@();failed_tests=@();race=[bool]$Race;integration=[bool]$Integration;checks=@{}}
$failure=$null
try{
    $env:GOMAXPROCS='2'
    $env:GOFLAGS='-p=1'
    Push-Location $backend
    try{
        Invoke-PortfolioCommand go @('mod','verify')
        $report.checks.modules='PASS'
        $testArguments=@('test','-p','1','-count=1','-timeout','300s','-json','./...')
        if($Race){$testArguments=@('test','-race','-p','1','-count=1','-timeout','300s','-json','./...')}
        $output=@(& go @testArguments 2>&1)
        $testExit=$LASTEXITCODE
        $events=@($output|ForEach-Object{try{ConvertFrom-Json -InputObject ([string]$_) -ErrorAction Stop}catch{}})
        $report.passed_tests=@($events|Where-Object{$_.Action -eq 'pass' -and $_.PSObject.Properties.Name -contains 'Test'}).Count
        $report.skipped_tests=@($events|Where-Object{$_.Action -eq 'skip' -and $_.PSObject.Properties.Name -contains 'Test'}|ForEach-Object{$_.Package+'/'+$_.Test})
        foreach($skipped in @($events|Where-Object{$_.Action -eq 'skip' -and $_.PSObject.Properties.Name -contains 'Test'})){
            $reason=@($events|Where-Object{$_.Action -eq 'output' -and $_.PSObject.Properties.Name -contains 'Test' -and $_.Test -eq $skipped.Test -and $_.Package -eq $skipped.Package}|ForEach-Object Output) -join ''
            $allowed=($skipped.Package -eq 'game-realtime-gm/backend/internal/database' -and $reason.Contains('dedicated Demo database required')) -or
                ($skipped.Package -eq 'game-realtime-gm/backend/internal/pve/gameplay' -and $reason.Contains('DC_INTEGRATION=1 and dedicated Demo storage required')) -or
                ($skipped.Package -eq 'game-realtime-gm/backend/tests/v2' -and ($reason.Contains('set PVE_INTEGRATION=1 for isolated ') -or ($skipped.Test -eq 'TestR4ActualBinarySwitchAndRollback' -and $reason.Contains('set both server binary paths for an actual R4/R3 rollback rehearsal'))))
            if(!$allowed){throw ('Unexpected backend test skip: '+$skipped.Package+'/'+$skipped.Test)}
        }
        $report.failed_tests=@($events|Where-Object{$_.Action -eq 'fail'}|ForEach-Object{if($_.PSObject.Properties.Name -contains 'Test'){$_.Package+'/'+$_.Test}else{$_.Package}})
        $output|Write-Output
        if($testExit -ne 0 -or $report.failed_tests.Count -gt 0){throw 'Backend tests failed; review emitted test output.'}
        if($report.passed_tests -lt 1){throw 'No backend tests executed.'}
        $report.checks.unit='PASS_WITH_RECORDED_INTEGRATION_SKIPS'
        Invoke-PortfolioCommand go @('vet','-p','1','./...')
        $report.checks.vet='PASS'
    }finally{Pop-Location}
    if($Integration){
        & (Join-Path $PSScriptRoot 'portfolio-ci-dc.ps1') -Race:$Race
        $report.checks.current_v8_storage='PASS'
    }
    $report.status='PASS'
}catch{
    $failure=$_
    $report.status='FAIL'
    $report.failed_tests+=@($_.Exception.Message)
}finally{
    foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}
    $report.completed_at=[DateTimeOffset]::UtcNow.ToString('o')
    if($ReportPath){[IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportPath),($report|ConvertTo-Json -Depth 8)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))}
    $report|ConvertTo-Json -Depth 8|Write-Output
}
if($failure){throw $failure}
