function Get-DcNativeArgumentString {
    param([AllowEmptyCollection()][AllowNull()][string[]]$Arguments = @())
    # Start-Process joins string arrays with spaces, losing argument boundaries.
    # Quote every argument using the Windows native command-line escaping rules:
    # double backslashes before a quote and before the final closing quote.
    $quoted = foreach ($argument in $Arguments) {
        $escaped = [regex]::Replace([string]$argument, '(\\*)"', '$1$1\"')
        $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
        '"' + $escaped + '"'
    }
    $quoted -join ' '
}
