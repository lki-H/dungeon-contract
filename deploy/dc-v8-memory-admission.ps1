# Read-only admission before a heavy child process or a new Run is created.
. (Join-Path $PSScriptRoot 'dc-v8-observation.ps1')
function Get-DcV8AdmissionDecision {
    param(
        [Parameter(Mandatory)][UInt64]$AvailableCommit,
        [Parameter(Mandatory)][UInt64]$AvailablePhysical,
        [ValidateRange(128,16384)][int]$IncrementalCommitMB,
        [ValidateRange(128,16384)][int]$IncrementalPhysicalMB
    )
    $requiredCommit=[UInt64](($IncrementalCommitMB+1024+128)*1MB)
    $requiredPhysical=[UInt64](($IncrementalPhysicalMB+512+128)*1MB)
    [pscustomobject]@{
        status=$(if($AvailableCommit-ge$requiredCommit -and $AvailablePhysical-ge$requiredPhysical){'ADMITTED'}else{'ENV_RESOURCE_BLOCKED'})
        available_commit_bytes=$AvailableCommit;available_physical_bytes=$AvailablePhysical
        required_commit_bytes=$requiredCommit;required_physical_bytes=$requiredPhysical
        incremental_commit_mb=$IncrementalCommitMB;incremental_physical_mb=$IncrementalPhysicalMB
        commit_reserve_mb=1024;physical_reserve_mb=512;jitter_mb=128
        budget_basis='conservative_initial_scheduling_estimate_not_performance_acceptance'
    }
}
function Get-DcV8WorkloadBudget {
    param([Parameter(Mandatory)][string]$Workload,[ValidateRange(1,4)][int]$Players=1,[ValidateRange(0,4)][int]$RenderClients=0)
    if($Players-notin@(1,2,4) -or $RenderClients-gt$Players){throw 'Invalid memory workload party/render count.'}
    switch($Workload){
        'Checks' {return @(1536,1024)}
        'Regression' {return @(1536,1024)}
        'Build' {return @(2048,1408)}
        'Test' {return @(768,512)}
        'Integration' {return @(896,640)}
        'IntegrationAll' {return @(896,640)}
        'AuthorityStandalone' {return @(768,512)}
        'Pressure' {return @(1024,768)}
        'ControlsCheck' {return @(512,384)}
        'UiCheck' {return @(1024,768)}
        'LobbyCapture' {return @(768,512)}
        'VerifyLobby' {return @(1152,896)}
        'PackageBuild' {return @(1024,768)}
        'LanHost' {return @(768,512)}
        {$_-in@('Verify','VerifyFull','VerifyPressure','Solo')} {return @((576+320*$Players+256*$RenderClients),(352+256*$Players+160*$RenderClients))}
        default {throw 'Unknown memory workload; explicit budget required.'}
    }
}
function Assert-DcV8MemoryAdmission {
    param([Parameter(Mandatory)][string]$Workload,[ValidateRange(1,4)][int]$Players=1,[ValidateRange(0,4)][int]$RenderClients=0)
    $budget=Get-DcV8WorkloadBudget -Workload $Workload -Players $Players -RenderClients $RenderClients
    $memory=Get-DcV8MemorySnapshot
    $decision=Get-DcV8AdmissionDecision -AvailableCommit $memory.availableCommit -AvailablePhysical $memory.availablePhysical -IncrementalCommitMB $budget[0] -IncrementalPhysicalMB $budget[1]
    $decision|Add-Member -NotePropertyName recorded_at -NotePropertyValue ([DateTimeOffset]::Now.ToOffset([TimeSpan]::FromHours(8)).ToString('o'))
    $decision|Add-Member -NotePropertyName workload -NotePropertyValue $Workload
    $decision|Add-Member -NotePropertyName players -NotePropertyValue $Players
    $decision|Add-Member -NotePropertyName rendered_clients -NotePropertyValue $RenderClients
    $decision|Add-Member -NotePropertyName owner_pid -NotePropertyValue $PID
    if(!(Get-Command Get-DcPaths -ErrorAction SilentlyContinue)){. (Join-Path $PSScriptRoot 'dc-common.ps1')}
    $folder=Join-Path (Get-DcPaths).Cache 'evidence'
    if(!(Test-Path -LiteralPath $folder -PathType Container)){throw 'Missing isolated evidence root.'}
    $path=Join-Path $folder ('v8-admission-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')+'-'+[Guid]::NewGuid().ToString('N')+'.json')
    $stream=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    $writer=[IO.StreamWriter]::new($stream,[Text.UTF8Encoding]::new($false))
    try{$writer.WriteLine(($decision|ConvertTo-Json -Depth 4))}finally{$writer.Dispose()}
    if($decision.status-ne'ADMITTED'){throw ('ENV_RESOURCE_BLOCKED: '+$Workload+'; no Run or heavy process started; admission evidence: '+$path)}
    Write-Output ('Memory admission ADMITTED: '+$Workload+'; evidence: '+$path)
}
