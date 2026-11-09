param()
. (Join-Path $PSScriptRoot 'dc-common.ps1')
$paths = Get-DcPaths
& (Join-Path $PSScriptRoot 'portfolio.ps1') -Action Prepare
$existingUnity = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($paths.UnityProject) })
if ($existingUnity.Count -gt 0) { throw 'Close this Demo Unity project before preparing its Library.' }
$libraryLink = Join-Path $paths.UnityProject 'Library'
if (Test-Path -LiteralPath $libraryLink) {
    $library = Get-Item -LiteralPath $libraryLink -Force
    if ($library.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath([string]@($library.Target)[0]) -ne $paths.Library) {
        throw 'Existing Library has not been moved; preparation will not overwrite or delete it.'
    }
}
foreach ($relative in @('', 'private', 'unity-library', 'upm', 'tmp', 'go-build', 'go-mod', 'builds', 'logs', 'evidence', 'imagegen\raw', 'imagegen\processed')) {
    $directory = if ($relative -eq 'private') { $paths.Private } elseif ($relative -eq 'unity-library') { $paths.Library } elseif ($relative) { Join-Path $paths.Cache $relative } else { $paths.Cache }
    if (!(Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
$system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
foreach ($protectedDirectory in @($paths.Private, (Join-Path $paths.Cache 'logs'), (Join-Path $paths.Cache 'evidence'))) {
    $directoryInfo = [IO.DirectoryInfo]::new($protectedDirectory)
    $acl = [IO.FileSystemAclExtensions]::GetAccessControl($directoryInfo, [Security.AccessControl.AccessControlSections]::Access)
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($existingRule in @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))) { $acl.RemoveAccessRuleAll($existingRule) }
    foreach ($sid in @($identity, $system)) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    [IO.FileSystemAclExtensions]::SetAccessControl($directoryInfo, $acl)
}
$required = @('root-password.txt', 'db-password.txt', 'redis-password.txt', 'jwt-secret.txt', 'service-token.txt', 'admission-key.txt', 'metrics-token.txt')
$existing = @($required | Where-Object { Test-Path -LiteralPath (Join-Path $paths.Private $_) })
if ($existing.Count -gt 0 -and $existing.Count -ne $required.Count) { throw 'Partial credential set detected; repair it explicitly instead of silently rotating an existing database password.' }
if ($existing.Count -eq 0) {
    if ([IO.Directory]::EnumerateFileSystemEntries($paths.Private).GetEnumerator().MoveNext()) { throw 'Private directory is not empty; refusing to replace unrecognized credentials.' }
    foreach ($file in $required) {
        $bytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        $secret = [Convert]::ToHexString($bytes).ToLowerInvariant()
        [IO.File]::WriteAllText((Join-Path $paths.Private $file), $secret, [Text.UTF8Encoding]::new($false))
    }
}
$redisPassword = [IO.File]::ReadAllText((Join-Path $paths.Private 'redis-password.txt')).Trim()
$redisConfiguration = "bind 0.0.0.0`nprotected-mode yes`nport 6379`ndir /data`nappendonly yes`nrequirepass $redisPassword`n"
$redisPath = Join-Path $paths.Private 'redis.conf'
if (Test-Path -LiteralPath $redisPath) {
    if ([IO.File]::ReadAllText($redisPath) -ne $redisConfiguration) { throw 'Existing private Redis configuration differs; no automatic overwrite.' }
} else {
    [IO.File]::WriteAllText($redisPath, $redisConfiguration, [Text.UTF8Encoding]::new($false))
}
if (!(Test-Path -LiteralPath $libraryLink)) { New-Item -ItemType Junction -Path $libraryLink -Target $paths.Library | Out-Null }
Assert-DcLayout
Write-Output 'Demo configured layout, Library junction and external credentials are ready. Global environment variables are unchanged.'
