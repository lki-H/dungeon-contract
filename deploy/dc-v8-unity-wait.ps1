function Wait-DcV8Unity {
    param([Parameter(Mandatory)][Diagnostics.Process]$Process,[Parameter(Mandatory)][string]$Log,[int]$TimeoutSeconds=1200)
    . (Join-Path $PSScriptRoot 'dc-v8-observation.ps1')
    $null=$Process.Handle
    $dcDeadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $dcMemoryWriter=[IO.StreamWriter]::new(($Log+'.memory.jsonl'),$false,[Text.UTF8Encoding]::new($false))
    $dcSamples=0;$dcPeakPrivate=[long]0;$dcMinCommit=[UInt64]::MaxValue;$dcMemoryStatus='completed'
    try {
        while(-not $Process.HasExited) {
            $Process.Refresh()
            $dcMemory=Get-DcV8MemorySnapshot
            $dcSamples++;$dcPeakPrivate=[Math]::Max($dcPeakPrivate,$Process.PrivateMemorySize64);$dcMinCommit=[Math]::Min($dcMinCommit,$dcMemory.availableCommit)
            $dcMemoryWriter.WriteLine(([pscustomobject]@{sample=$dcSamples;private_bytes=$Process.PrivateMemorySize64;working_set_bytes=$Process.WorkingSet64;available_commit_bytes=$dcMemory.availableCommit;available_physical_bytes=$dcMemory.availablePhysical}|ConvertTo-Json -Compress));$dcMemoryWriter.Flush()
            if($dcMemory.availableCommit-lt1GB -or $dcMemory.availablePhysical-lt512MB){throw ('ENV_RESOURCE_BLOCKED: Unity stopped at the unchanged system reserve; retained log: '+$Log)}
            if(Test-Path -LiteralPath $Log) {
                $dcTail=Get-Content -LiteralPath $Log -Tail 12 -ErrorAction SilentlyContinue
                if($dcTail -match 'Licensing initialization failed|Failed to handshake to channel') {
                    throw ('Unity licensing IPC failed before compilation. Retained log: '+$Log)
                }
            }
            if([DateTime]::UtcNow -ge $dcDeadline){throw ('Unity deadline exceeded. Retained log: '+$Log)}
            $null=$Process.WaitForExit(1000)
        }
        if($Process.ExitCode -ne 0){throw ('Unity failed. Retained log: '+$Log)}
    } catch {
        $dcMemoryStatus=if($_.Exception.Message.StartsWith('ENV_RESOURCE_BLOCKED:')){'ENV_RESOURCE_BLOCKED'}else{'failed'}
        throw
    } finally {
        if(-not $Process.HasExited){Stop-Process -InputObject $Process -ErrorAction SilentlyContinue;$null=$Process.WaitForExit(5000)}
        $dcMemoryWriter.Dispose()
        [IO.File]::WriteAllText(($Log+'.memory.json'),([pscustomobject]@{status=$dcMemoryStatus;sample_count=$dcSamples;peak_private_bytes=$dcPeakPrivate;minimum_available_commit_bytes=$dcMinCommit;commit_reserve_mb=1024;physical_reserve_mb=512;samples=([IO.Path]::GetFileName($Log)+'.memory.jsonl')}|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
    }
}
