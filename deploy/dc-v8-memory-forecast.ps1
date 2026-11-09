# Pure orchestration overhead accounting; no processes, containers or files change.
function Get-DcV8OrchestrationForecast {
    param([Parameter(Mandatory)][UInt64]$AvailableCommit,[Parameter(Mandatory)][UInt64]$AvailablePhysical)
    [pscustomobject]@{
        available_commit_bytes=[UInt64][Math]::Max([long]0,([long]$AvailableCommit-256MB))
        available_physical_bytes=[UInt64][Math]::Max([long]0,([long]$AvailablePhysical-128MB))
    }
}
