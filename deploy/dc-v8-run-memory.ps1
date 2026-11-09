param([Parameter(Mandatory)][string]$Evidence,[Parameter(Mandatory)][int]$OwnerPid)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dc-v8-observation.ps1')
if(!(Get-Command Get-DcPaths -ErrorAction SilentlyContinue)){. (Join-Path $PSScriptRoot 'dc-common.ps1')}
$dcRoot=[IO.Path]::GetFullPath((Join-Path (Get-DcPaths).Cache 'evidence'))+[IO.Path]::DirectorySeparatorChar
$dcFolder=[IO.Path]::GetFullPath($Evidence)
if(!$dcFolder.StartsWith($dcRoot,[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $dcFolder -PathType Container)){throw 'Invalid isolated evidence directory.'}
$dcParent=Get-Process -Id $OwnerPid -ErrorAction Stop
$dcParentStarted=$dcParent.StartTime.ToUniversalTime().Ticks
$dcParent.Dispose()
$dcClock=[Diagnostics.Stopwatch]::StartNew()
$dcStatus='observing';$dcRejected=0;$dcCount=0;$dcPeaks=@{};$dcMinCommit=[UInt64]::MaxValue;$dcMinPhysical=[UInt64]::MaxValue
$dcOutput=Join-Path $dcFolder 'process-memory.json'
$dcKnown=@{control='dc-control.exe';server='GameService/GameService.exe';'client-0'='DungeonContract/DungeonContract.exe';'client-1'='DungeonContract/DungeonContract.exe';'client-2'='DungeonContract/DungeonContract.exe';'client-3'='DungeonContract/DungeonContract.exe'}
$dcWriter=[IO.StreamWriter]::new((Join-Path $dcFolder 'process-memory.samples.jsonl'),$false,[Text.UTF8Encoding]::new($false))
function Read-DcV8Utc($Value){if($Value -is [datetime]){return $Value.ToUniversalTime()};if($Value -is [datetimeoffset]){return $Value.UtcDateTime};[DateTimeOffset]::Parse([string]$Value,[Globalization.CultureInfo]::InvariantCulture).UtcDateTime}
function Save-DcV8RunMemory {
    $dcDocument=[pscustomobject]@{profile='dc_ruins.v8.p5.1';scope='external_owned_processes_and_system_commit';status=$dcStatus;identity_rejections=$dcRejected;sample_interval_seconds=1;sample_count=$dcCount;samples_file='process-memory.samples.jsonl';duration_seconds=$dcClock.Elapsed.TotalSeconds;minimum_available_commit_bytes=$dcMinCommit;minimum_available_physical_bytes=$dcMinPhysical;peak_private_bytes_by_role=$dcPeaks;commit_reserve_mb=1024;physical_reserve_mb=512}
    [IO.File]::WriteAllText($dcOutput,($dcDocument|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
}
try {
    while($dcClock.Elapsed.TotalSeconds -lt 2400) {
        if(Test-Path -LiteralPath (Join-Path $dcFolder 'memory-done')){$dcStatus='completed';break}
        $dcOwner=Get-Process -Id $OwnerPid -ErrorAction SilentlyContinue
        if(!$dcOwner){$dcStatus='owner_exited';break}
        try{$dcSameOwner=$dcOwner.StartTime.ToUniversalTime().Ticks -eq $dcParentStarted}finally{$dcOwner.Dispose()}
        if(!$dcSameOwner){$dcStatus='owner_identity_changed';break}
        $dcMemory=Get-DcV8MemorySnapshot
        $dcMinCommit=[Math]::Min($dcMinCommit,$dcMemory.availableCommit);$dcMinPhysical=[Math]::Min($dcMinPhysical,$dcMemory.availablePhysical)
        $dcProcesses=[Collections.Generic.List[object]]::new()
        $dcRoster=Join-Path $dcFolder 'process-roster.json'
        if((Test-Path -LiteralPath $dcRoster) -and (Get-Item -LiteralPath $dcRoster).Length -le 16384) {
            try{$dcEntries=@(Get-Content -LiteralPath $dcRoster -Raw|ConvertFrom-Json -ErrorAction Stop)}catch{$dcEntries=@()}
            foreach($dcEntry in $dcEntries) {
                if($null -eq $dcEntry){continue}
                if(!$dcKnown.ContainsKey([string]$dcEntry.role)){$dcRejected++;continue}
                $dcExpected=[IO.Path]::GetFullPath((Join-Path (Join-Path (Get-DcPaths).Cache 'builds/v8-p5') $dcKnown[$dcEntry.role]))
                $dcProcess=Get-Process -Id ([int]$dcEntry.pid) -ErrorAction SilentlyContinue
                if(!$dcProcess){continue}
                try {
                    $dcBefore=(Read-DcV8Utc $dcEntry.started_before).AddSeconds(-2);$dcAfter=(Read-DcV8Utc $dcEntry.started_after).AddSeconds(2)
                    $dcStarted=$dcProcess.StartTime.ToUniversalTime()
                    if([IO.Path]::GetFullPath($dcProcess.Path) -ne $dcExpected -or [IO.Path]::GetFullPath($dcEntry.binary) -ne $dcExpected -or $dcStarted -lt $dcBefore -or $dcStarted -gt $dcAfter){$dcRejected++;continue}
                    $dcPrivate=$dcProcess.PrivateMemorySize64
                    if(!$dcPeaks.ContainsKey($dcEntry.role) -or $dcPrivate -gt $dcPeaks[$dcEntry.role]){$dcPeaks[$dcEntry.role]=$dcPrivate}
                    $dcProcesses.Add([pscustomobject]@{role=$dcEntry.role;pid=$dcProcess.Id;private_bytes=$dcPrivate;working_set_bytes=$dcProcess.WorkingSet64;threads=$dcProcess.Threads.Count;handles=$dcProcess.HandleCount;cpu_seconds=$dcProcess.CPU})
                } catch {if(!$dcProcess.HasExited){throw}}finally{$dcProcess.Dispose()}
            }
        }
        $dcCount++
        $dcRow=[pscustomobject]@{seconds=[Math]::Round($dcClock.Elapsed.TotalSeconds,3);available_physical_bytes=$dcMemory.availablePhysical;available_commit_bytes=$dcMemory.availableCommit;commit_limit_bytes=$dcMemory.totalCommit;processes=$dcProcesses.ToArray()}
        $dcWriter.WriteLine(($dcRow|ConvertTo-Json -Depth 6 -Compress));$dcWriter.Flush()
        if($dcMemory.availableCommit -lt 1GB -or $dcMemory.availablePhysical -lt 512MB){$dcStatus='memory_guard_triggered';[IO.File]::WriteAllText((Join-Path $dcFolder 'memory-stop.json'),'{"reason":"system_memory_reserve_reached"}');break}
        if($dcCount%5 -eq 1){Save-DcV8RunMemory}
        Start-Sleep -Milliseconds 1000
    }
    if($dcClock.Elapsed.TotalSeconds -ge 2400){$dcStatus='observer_deadline_exceeded'}
} finally {$dcWriter.Dispose();$dcClock.Stop();Save-DcV8RunMemory}
