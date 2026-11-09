# Observe only the process created by the caller. Never stop unrelated processes.
function Get-DcV8MemorySnapshot {
    if(-not ('DcV8MemoryStatus' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DcV8MemoryStatus {
    [StructLayout(LayoutKind.Sequential)] public struct Status {
        public uint length, load;
        public ulong totalPhysical, availablePhysical, totalCommit, availableCommit, totalVirtual, availableVirtual, extended;
    }
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GlobalMemoryStatusEx(ref Status value);
    public static Status Read() {
        var value=new Status(); value.length=(uint)Marshal.SizeOf(typeof(Status));
        if(!GlobalMemoryStatusEx(ref value)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return value;
    }
}
'@
    }
    [DcV8MemoryStatus]::Read()
}
function Wait-DcV8ObservedProcess {
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$Process,
        [Parameter(Mandatory)][string]$Output,
        [ValidateRange(1,3600)][int]$TimeoutSeconds=240,
        [ValidateRange(256,8192)][int]$CommitReserveMB=1024
    )
    $null=$Process.Handle
    $dcClock=[Diagnostics.Stopwatch]::StartNew()
    $dcRows=[Collections.Generic.List[object]]::new()
    $dcReason='completed'
    try {
        while(-not $Process.HasExited) {
            $Process.Refresh()
            $dcMemory=Get-DcV8MemorySnapshot
            $dcRows.Add([pscustomobject]@{
                seconds=[Math]::Round($dcClock.Elapsed.TotalSeconds,3)
                private_bytes=$Process.PrivateMemorySize64
                working_set_bytes=$Process.WorkingSet64
                threads=$Process.Threads.Count
                handles=$Process.HandleCount
                available_physical_bytes=$dcMemory.availablePhysical
                available_commit_bytes=$dcMemory.availableCommit
                commit_limit_bytes=$dcMemory.totalCommit
            })
            if($dcMemory.availableCommit -lt [UInt64]($CommitReserveMB*1MB) -or $dcMemory.availablePhysical -lt 512MB) {
                $dcReason='stopped_before_system_memory_exhaustion'
                Stop-Process -InputObject $Process -ErrorAction SilentlyContinue
                break
            }
            if($dcClock.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                $dcReason='deadline_exceeded'
                Stop-Process -InputObject $Process -ErrorAction SilentlyContinue
                break
            }
            $null=$Process.WaitForExit(1000)
        }
        $Process.WaitForExit()
        if($dcReason -eq 'completed' -and $Process.ExitCode -ne 0){$dcReason='nonzero_exit'}
    } finally {
        $dcClock.Stop()
        $dcRecord=[pscustomobject]@{
            profile='dc_ruins.v8.p5.1'
            scope='external_owned_process_memory_observation'
            status=$dcReason
            pid=$Process.Id
            exit_code=$(if($Process.HasExited){$Process.ExitCode}else{$null})
            duration_seconds=$dcClock.Elapsed.TotalSeconds
            commit_reserve_mb=$CommitReserveMB
            allocation_counter='not_available_in_this_external_sampler'
            samples=$dcRows.ToArray()
        }
        [IO.File]::WriteAllText($Output,($dcRecord|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    }
    if($dcReason -ne 'completed'){throw ('Owned validation process '+$dcReason+'; memory evidence: '+$Output)}
}
