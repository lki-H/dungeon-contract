Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dc-native-arguments.ps1')

function Assert-DcExternalPath([string]$Path,[string]$Project,[string]$Label) {
    $comparison = [StringComparison]::OrdinalIgnoreCase
    $normalized = [IO.Path]::GetFullPath($Path).TrimEnd('\','/')
    $source = [IO.Path]::GetFullPath($Project).TrimEnd('\','/')
    if ($normalized.Equals($source,$comparison) -or $normalized.StartsWith($source+'\',$comparison) -or $source.StartsWith($normalized+'\',$comparison)) { throw "$Label must be separate from the source tree." }
    # GetFullPath is lexical: reject linked ancestors before creating directories or
    # reading credentials, including when the final directory does not exist yet.
    $cursor = $normalized
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "$Label cannot have a reparse-point ancestor." }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if (!$parent -or $parent -eq $cursor) { break }
        $cursor = $parent
    }
}

function Get-DcPaths {
    $projectValue = if ($env:DC_PROJECT_ROOT) { $env:DC_PROJECT_ROOT } else { Join-Path $PSScriptRoot '..' }
    $project = [IO.Path]::GetFullPath($projectValue).TrimEnd('\','/')
    if (!(Test-Path -LiteralPath (Join-Path $project 'backend/go.mod') -PathType Leaf) -or !(Test-Path -LiteralPath (Join-Path $project 'docs/game-demo/config/demo-v8-p5.json') -PathType Leaf)) { throw 'DC_PROJECT_ROOT must point to the Demo source root.' }
    $legacy = $project.Equals('E:\game-realtime-gm-demo', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath 'D:\dev-cache\dungeon-contract\private' -PathType Container)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $digest = $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($project.ToLowerInvariant())) } finally { $hasher.Dispose() }
    $identity = [BitConverter]::ToString($digest).Replace('-', '').Substring(0, 12).ToLowerInvariant()
    $localRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('DungeonContract\projects\' + $identity)
    $cacheValue = if ($env:DC_CACHE_ROOT) { $env:DC_CACHE_ROOT } elseif ($legacy) { 'D:\dev-cache\dungeon-contract' } else { Join-Path $localRoot 'cache' }
    $cache = [IO.Path]::GetFullPath($cacheValue)
    $privateValue = if ($env:DC_PRIVATE_ROOT) { $env:DC_PRIVATE_ROOT } elseif ($legacy -and !$env:DC_CACHE_ROOT) { 'D:\dev-cache\dungeon-contract\private' } else { Join-Path $cache 'private' }
    $libraryValue = if ($env:DC_LIBRARY_ROOT) { $env:DC_LIBRARY_ROOT } else { Join-Path $cache 'unity-library' }
    $unityValue = if ($env:DC_UNITY_PATH) { $env:DC_UNITY_PATH } elseif ($legacy) { 'D:\unity\Editor\6000.3.25f1\Editor\Unity.exe' } else { Join-Path ${env:ProgramFiles} 'Unity Hub\Editor\6000.3.25f1\Editor\Unity.exe' }
    $privatePath = [IO.Path]::GetFullPath($privateValue)
    Assert-DcExternalPath $cache $project 'The cache directory'
    Assert-DcExternalPath $privatePath $project 'The private directory'
    Assert-DcExternalPath $libraryValue $project 'The Unity Library directory'
    [pscustomobject]@{
        Project = $project
        Cache = $cache
        Private = $privatePath
        Library = [IO.Path]::GetFullPath($libraryValue)
        Unity = [IO.Path]::GetFullPath($unityValue)
        UnityProject = Join-Path $project 'client\unity-demo'
        Compose = Join-Path $project 'deploy\docker-compose.dc-demo.yml'
        PathPolicy = Join-Path $project 'deploy\portfolio-path-policy.json'
    }
}

function Read-DcPlaySession {
    $paths = Get-DcPaths
    $sessionPath = Join-Path $paths.Private 'p1-play-session.json'
    if (!(Test-Path -LiteralPath $sessionPath)) { return @() }
    if ((Get-Item -LiteralPath $sessionPath).Length -gt 65536) { throw 'Demo session journal exceeds its size limit.' }
    $document = [IO.File]::ReadAllText($sessionPath)
    if (!$document.TrimStart().StartsWith('[')) { throw 'Demo process journal must be an array.' }
    $null = ConvertFrom-Json -InputObject $document
    # Preserve array elements, including null, across PowerShell 5.1 and 7.
    $decoded = (ConvertFrom-Json -InputObject ('{"entries":' + $document + '}')).entries
    $session = @()
    foreach ($record in $decoded) { $session += $record }
    if ($session.Count -gt 3) { throw 'Unexpected Demo session roster.' }
    $roles = @{}
    foreach ($entry in $session) {
        if ($null -eq $entry -or !$entry.PSObject.Properties['role'] -or !$entry.PSObject.Properties['pid'] -or !$entry.PSObject.Properties['started_utc_ticks'] -or !$entry.PSObject.Properties['path']) { throw 'Invalid Demo process journal record.' }
        $expected = switch ($entry.role) {
            'control' { Join-Path $paths.Cache 'builds\dc-control.exe' }
            'service' { Join-Path $paths.Cache 'builds\p1\GameService\GameService.exe' }
            'client' { Join-Path $paths.Cache 'builds\p1\DungeonContract\DungeonContract.exe' }
            default { throw 'Unexpected Demo process role.' }
        }
        if ($roles.ContainsKey($entry.role) -or [int]$entry.pid -le 0 -or [long]$entry.started_utc_ticks -le 0 -or [IO.Path]::GetFullPath($entry.path) -ne $expected) { throw 'Invalid Demo process journal; automatic recovery refused.' }
        $roles[$entry.role] = $true
    }
    $session
}

function Resolve-DcPlayProcess {
    param($Entry)
    $process = Get-Process -Id $Entry.pid -ErrorAction SilentlyContinue
    if (!$process) { return $null }
    if ([IO.Path]::GetFullPath($process.Path) -ne [IO.Path]::GetFullPath($Entry.path) -or $process.StartTime.ToUniversalTime().Ticks -ne $Entry.started_utc_ticks) { throw 'Recorded PID belongs to a different process; refusing to stop or reuse it.' }
    $process
}

function Get-DcPlayState {
    $entries = @(Read-DcPlaySession)
    $running = @{}
    foreach ($entry in $entries) { $running[$entry.role] = $null -ne (Resolve-DcPlayProcess $entry) }
    $state = 'stopped'
    if (@($running.Values | Where-Object { $_ }).Count -gt 0) {
        $state = 'restart'
        if ($running['control'] -and $running['service']) {
            $state = if ($running['client']) { 'running' } else { 'resume_client' }
        }
    }
    [pscustomobject]@{State = $state; Entries = $entries; Running = $running}
}

function Assert-DcLayout {
    $paths = Get-DcPaths
    foreach ($relative in @('private', 'unity-library', 'upm', 'tmp', 'go-build', 'go-mod', 'builds', 'logs', 'evidence', 'imagegen\raw', 'imagegen\processed')) {
        $directory = if ($relative -eq 'private') { $paths.Private } elseif ($relative -eq 'unity-library') { $paths.Library } else { Join-Path $paths.Cache $relative }
        if (!(Test-Path -LiteralPath $directory -PathType Container)) { throw 'Run dc-prepare.ps1 first.' }
    }
    $library = Get-Item -LiteralPath (Join-Path $paths.UnityProject 'Library') -Force
    if ($library.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath([string]@($library.Target)[0]) -ne $paths.Library) {
        throw 'Unity Library must point to the configured Demo Library directory.'
    }
    foreach ($file in @('root-password.txt', 'db-password.txt', 'redis-password.txt', 'redis.conf', 'jwt-secret.txt', 'service-token.txt', 'admission-key.txt', 'metrics-token.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $paths.Private $file) -PathType Leaf)) { throw 'Demo credentials are incomplete; do not regenerate credentials for an existing volume.' }
    }
    $acl = Get-Acl -LiteralPath $paths.Private
    if (!$acl.AreAccessRulesProtected) { throw 'Demo private directory must disable inherited access.' }
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    foreach ($rule in $acl.Access) {
        $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        if ($rule.AccessControlType -eq 'Allow' -and $sid -notin @($currentSid, 'S-1-5-18')) { throw 'Demo private directory permits an unexpected identity.' }
    }
    $secretValues = @('root-password.txt', 'db-password.txt', 'redis-password.txt', 'jwt-secret.txt', 'service-token.txt', 'admission-key.txt', 'metrics-token.txt') | ForEach-Object {
        $secret = [IO.File]::ReadAllText((Join-Path $paths.Private $_)).Trim()
        if ($secret -notmatch '^[a-f0-9]{64}$') { throw 'Demo credentials must be independent generated 32-byte hex values.' }
        $secret
    }
    if (@($secretValues | Sort-Object -Unique).Count -ne $secretValues.Count) { throw 'Demo credential reuse is forbidden.' }
}

function Assert-DcBackendLayout {
    $paths = Get-DcPaths
    foreach ($relative in @('private', 'go-build', 'go-mod', 'tmp', 'builds', 'logs', 'evidence')) {
        $target = if ($relative -eq 'private') { $paths.Private } else { Join-Path $paths.Cache $relative }
        if (!(Test-Path -LiteralPath $target -PathType Container)) { throw "Run deploy/portfolio.ps1 -Action Prepare first; missing $target." }
    }
    foreach ($file in @('root-password.txt', 'db-password.txt', 'redis-password.txt', 'redis.conf', 'jwt-secret.txt', 'service-token.txt', 'admission-key.txt', 'metrics-token.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $paths.Private $file) -PathType Leaf)) { throw 'Demo credentials are incomplete; run the portable Prepare action.' }
    }
    $secretValues = @('root-password.txt', 'db-password.txt', 'redis-password.txt', 'jwt-secret.txt', 'service-token.txt', 'admission-key.txt', 'metrics-token.txt') | ForEach-Object {
        $secret = [IO.File]::ReadAllText((Join-Path $paths.Private $_)).Trim()
        if ($secret -notmatch '^[a-f0-9]{64}$') { throw 'Demo credentials must be independent generated 32-byte hex values.' }
        $secret
    }
    if (@($secretValues | Sort-Object -Unique).Count -ne $secretValues.Count) { throw 'Demo credential reuse is forbidden.' }
}

function Get-DcEnvironment {
    param([switch]$IncludeCredentials, [ValidateSet('v2','v8')][string]$Runtime = 'v2')
    $paths = Get-DcPaths
    $configName = if ($Runtime -eq 'v8') { 'demo-v8-p5.json' } else { 'demo-v2.json' }
    $values = @{
        DC_PROJECT_ROOT = $paths.Project
        DC_CACHE_ROOT = $paths.Cache
        DC_PRIVATE_ROOT = $paths.Private
        DC_LIBRARY_ROOT = $paths.Library
        DC_UNITY_PATH = $paths.Unity
        GOCACHE = Join-Path $paths.Cache 'go-build'
        GOMODCACHE = Join-Path $paths.Cache 'go-mod'
        GOTMPDIR = Join-Path $paths.Cache 'tmp'
        TEMP = Join-Path $paths.Cache 'tmp'
        TMP = Join-Path $paths.Cache 'tmp'
        UPM_CACHE_ROOT = Join-Path $paths.Cache 'upm'
        UPM_MAX_CACHE_SIZE = '5000000000'
        GOTOOLCHAIN = 'local'
        GOMAXPROCS = '2'
        DC_PRIVATE_DIR = $paths.Private.Replace('\', '/')
        DC_CONFIG_PATH = Join-Path $paths.Project ('docs\game-demo\config\' + $configName)
		DC_GAME_ENABLED = 'true'
        GAMEPLAY_MODE = 'v2'
        GIN_MODE = 'release'
        APP_HOST = '127.0.0.1'
        APP_PORT = '12880'
        DB_HOST = '127.0.0.1'
        DB_PORT = '23316'
        DB_USER = 'dc_demo'
        DB_NAME = 'game_realtime_dc_demo'
        REDIS_ADDR = '127.0.0.1:26389'
        REDIS_DB = '0'
        PVE_TEST_EVENTS_ENABLED = 'false'
        PVE_TEST_EVENTS_TOKEN = $null
        PVE_RULES_PATH = $null
        PVE_METRICS_ENABLED = 'false'
        PVE_METRICS_ADDR = '127.0.0.1:12891'
        PVE_ARCHIVE_ENABLED = 'false'
        PPROF_ENABLED = 'false'
        PVE_INTEGRATION = $null
        DC_INTEGRATION = $null
        DC_V3_P0_TEST = $null
        DC_V3_P1_TEST = $null
        V2_MIGRATION_CONFIRM = $null
        DC_V3_P0_CONFIRM = $null
        COMPOSE_PROJECT_NAME = 'dc-demo'
        COMPOSE_FILE = $null
        COMPOSE_ENV_FILES = $null
        DOCKER_CONTEXT = if ($env:DOCKER_CONTEXT) { $env:DOCKER_CONTEXT } else { $null }
        DOCKER_HOST = $null
        DOCKER_TLS_VERIFY = $null
        DOCKER_CERT_PATH = $null
    }
    if ($IncludeCredentials) {
        foreach ($binding in @{
            DB_PASSWORD = 'db-password.txt'
            REDIS_PASSWORD = 'redis-password.txt'
            JWT_SECRET = 'jwt-secret.txt'
            DC_SERVICE_TOKEN = 'service-token.txt'
            DC_ADMISSION_KEY = 'admission-key.txt'
            PVE_METRICS_TOKEN = 'metrics-token.txt'
        }.GetEnumerator()) {
            $values[$binding.Key] = [IO.File]::ReadAllText((Join-Path $paths.Private $binding.Value)).Trim()
        }
    }
    $values
}

function Invoke-DcNative {
    param([string]$Executable, [string[]]$Arguments, [string]$LogPath)
    if ($LogPath) {
        [IO.File]::WriteAllText($LogPath, '', [Text.UTF8Encoding]::new($false))
        & $Executable @Arguments 2>&1 | Tee-Object -FilePath $LogPath
    } else {
        & $Executable @Arguments
    }
    if ($LASTEXITCODE -ne 0) { throw "Command failed: $Executable (exit $LASTEXITCODE)." }
}

function Get-DcComposeArguments {
    $paths = Get-DcPaths
    @('compose', '--project-name', 'dc-demo', '--env-file', (Join-Path $paths.Project 'deploy\dc-empty.env'), '--file', $paths.Compose)
}

function Assert-DcCompose {
    $arguments = Get-DcComposeArguments
    $json = & docker @arguments config --format json
    if ($LASTEXITCODE -ne 0) { throw 'Demo Compose validation failed.' }
    $configuration = $json | ConvertFrom-Json
    if ($configuration.name -ne 'dc-demo' -or @($configuration.services.PSObject.Properties).Count -ne 2) { throw 'Unexpected Compose project or services.' }
    foreach ($serviceName in @('mysql', 'redis')) {
        $service = $configuration.services.$serviceName
        $expectedPort = if ($serviceName -eq 'mysql') { 23316 } else { 26389 }
        $ports = @($service.ports)
        if ($ports.Count -ne 1 -or $ports[0].host_ip -ne '127.0.0.1' -or [int]$ports[0].published -ne $expectedPort) { throw 'Demo Compose must publish only dedicated loopback ports.' }
        if ($service.PSObject.Properties.Name -contains 'container_name') { throw 'Demo Compose must use project-owned names.' }
    }
    if ($configuration.services.mysql.environment.MYSQL_DATABASE -ne 'game_realtime_dc_demo' -or $configuration.services.mysql.environment.MYSQL_USER -ne 'dc_demo') { throw 'Unexpected Demo MySQL target.' }
    foreach ($volume in $configuration.volumes.PSObject.Properties) {
        if ($volume.Value.name -ne ('dc-demo_' + $volume.Name)) { throw 'Demo volumes must belong to dc-demo.' }
    }
    foreach ($secret in $configuration.secrets.PSObject.Properties) {
        $file = [IO.Path]::GetFullPath([string]$secret.Value.file)
        $expectedRoot = (Get-DcPaths).Private + '\'
        if (!$file.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Secrets must remain in the external Demo private directory.' }
    }
}
