import contextlib
import importlib.util
import io
import json
import os
import re
import shutil
import stat
import subprocess
import sys
from types import SimpleNamespace
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import dc_v8_build_identity as BUILD


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


PACKAGE = load('v8_package_audit', 'dc-v8-package-audit.py')
SOURCE = load('v8_source_audit', 'dc-v8-source-audit.py')


class ToolingAuditTests(unittest.TestCase):
    def test_native_start_process_preserves_spaced_paths_quotes_and_backslashes(self):
        script = r'''
$ErrorActionPreference='Stop'
. (Join-Path $env:DC_ARGUMENTS_DEPLOY 'dc-native-arguments.ps1')
$dcValues=(ConvertFrom-Json $env:DC_ARGUMENTS_VALUES).values
$dcArguments=@($env:DC_ARGUMENTS_PROBE)+@($dcValues)
$dcStart=@{FilePath=$env:DC_ARGUMENTS_PYTHON;ArgumentList=@($dcArguments|Where-Object{$_ -ne ''});RedirectStandardOutput=$env:DC_ARGUMENTS_OUTPUT;RedirectStandardError=$env:DC_ARGUMENTS_ERROR;Wait=$true;PassThru=$true}
if($env:OS -eq 'Windows_NT'){$dcStart.WindowStyle='Hidden'}
$dcRaw=Start-Process @dcStart
Write-Output ('raw_argument_probe_exit='+$dcRaw.ExitCode)
$dcStart.ArgumentList=Get-DcNativeArgumentString $dcArguments
$dcQuoted=Start-Process @dcStart
if($dcQuoted.ExitCode -ne 0){throw 'Quoted native argument probe failed.'}
'''
        executables = ['pwsh']
        if os.name == 'nt':
            executables.append(str(Path(os.environ['SystemRoot']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'))
        values = ['', 'alpha beta', 'C:\\trailing slash\\', 'say "hi"', 'C:\\slash\\"quote', '中文 路径']
        for executable in executables:
            with self.subTest(executable=executable), tempfile.TemporaryDirectory(prefix='dc native arguments ') as temporary:
                root = Path(temporary)
                probe, output, error = root / 'argument probe.py', root / 'output.json', root / 'error.txt'
                probe.write_text('import json,sys; print(json.dumps(sys.argv[1:],ensure_ascii=True))', encoding='utf-8')
                environment = dict(os.environ, DC_ARGUMENTS_DEPLOY=str(Path(__file__).resolve().parent),
                                   DC_ARGUMENTS_PROBE=str(probe), DC_ARGUMENTS_OUTPUT=str(output), DC_ARGUMENTS_ERROR=str(error),
                                   DC_ARGUMENTS_PYTHON=sys.executable, DC_ARGUMENTS_VALUES=json.dumps({'values': values}))
                result = subprocess.run([executable, '-NoProfile', '-Command', script], env=environment, capture_output=True, text=True, timeout=20)
                details = result.stdout + result.stderr + (error.read_text(encoding='utf-8') if error.exists() else '')
                self.assertEqual(result.returncode, 0, details)
                self.assertEqual(json.loads(output.read_text(encoding='utf-8')), values)
                print(Path(executable).stem + ': ' + result.stdout.strip() + '; quoted native arguments preserved')

    def manifest(self, root, names):
        rows = [{'path': name, 'bytes': (root / name).stat().st_size, 'sha256': PACKAGE.digest(root / name)} for name in names]
        (root / 'package-manifest.json').write_text(json.dumps({'profile': 'dc_ruins.v8.p5.1', 'files': rows}), encoding='utf-8')

    def source_fixture(self, root):
        names = ['backend/go.mod', 'backend/go.sum', 'backend/internal/pve/task/rules.json',
                 'backend/internal/pve/task/rules-v2.json', 'backend/internal/pve/task/future-embedded.bin',
                 'client/unity-demo/Packages/manifest.json',
                 'client/unity-demo/Packages/packages-lock.json', 'client/unity-demo/Assets/DungeonContract/Editor/DcBuild.cs',
                 'client/unity-demo/Assets/DungeonContract/Editor/DcP5Imports.cs',
                 'client/unity-demo/Assets/Scripts/PveControlClient.cs',
                 'client/unity-demo/Assets/Scripts/PveControlClient.cs.meta',
                 'client/unity-demo/Assets/Resources/legal.txt',
                 'docs/game-demo/contracts/planned-v3/parameters.json',
                 'docs/game-demo/contracts/planned-v4/parameters.json']
        names += ['docs/game-demo/config/' + name for name in BUILD.CONFIGS]
        for name in names:
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('{}', encoding='utf-8')
        (root / 'docs/game-demo/source-resources.json').write_text(json.dumps({
            'schema_version': 1, 'required_paths': ['client/unity-demo/Assets/Resources/legal.txt'],
            'optional_components': {}}), encoding='utf-8')

    def bound_build(self, root, source, guid):
        names = ['dc-control.exe']
        for role in ('DungeonContract', 'GameService'):
            names += [f'{role}/{role}.exe', f'{role}/UnityPlayer.dll',
                      f'{role}/{role}_Data/Managed/Assembly-CSharp.dll', f'{role}/{role}_Data/boot.config']
            names += [f'{role}/Content/{name}' for name in BUILD.CONFIGS]
        for name in names:
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            if '/Content/' in name:
                path.write_bytes((source / 'docs/game-demo/config' / path.name).read_bytes())
            else:
                path.write_text('build-guid=' + guid if name.endswith('boot.config') else name, encoding='utf-8')
        receipt = {'schema_version': 2, 'profile': BUILD.PROFILE, 'status': 'SUCCESSFUL_BUILD_BOUND_TO_INPUTS',
                   'unity_build_source': 'ordinary_Unity_build', 'sources': BUILD.source_rows(source),
                   'artifacts': BUILD.artifacts(root)}
        (root / 'v8-build-receipt.json').write_text(json.dumps(receipt), encoding='utf-8')

    def test_build_receipt_invalidates_shared_client_importer_meta_and_copied_contract(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, build = root / 'source', root / 'build'
            self.source_fixture(source)
            self.bound_build(build, source, 'ordinary')
            self.assertEqual(BUILD.validate_build(build, source), [])
            for name in ('client/unity-demo/Assets/Scripts/PveControlClient.cs',
                         'client/unity-demo/Assets/Scripts/PveControlClient.cs.meta',
                         'client/unity-demo/Assets/DungeonContract/Editor/DcP5Imports.cs',
                         'backend/internal/pve/task/rules.json',
                         'backend/internal/pve/task/rules-v2.json',
                         'backend/internal/pve/task/future-embedded.bin',
                         'docs/game-demo/contracts/planned-v3/parameters.json',
                         'docs/game-demo/contracts/planned-v4/parameters.json'):
                with self.subTest(name=name):
                    path = source / name
                    original = path.read_bytes()
                    path.write_bytes(original + b'changed')
                    self.assertIn('changed:' + name, BUILD.validate_build(build, source))
                    path.write_bytes(original)

    def test_source_receipt_rejects_windows_reparse_files_and_directories(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.source_fixture(root)
            original_lstat = Path.lstat
            for name in ('client/unity-demo/Assets/Scripts',
                         'client/unity-demo/Assets/Scripts/PveControlClient.cs.meta',
                         'docs/game-demo/contracts/planned-v3/parameters.json'):
                target = root / name

                def lstat(path, *args, **kwargs):
                    result = original_lstat(path, *args, **kwargs)
                    if path == target:
                        return SimpleNamespace(st_mode=result.st_mode, st_file_attributes=stat.FILE_ATTRIBUTE_REPARSE_POINT)
                    return result

                with self.subTest(name=name), patch.object(Path, 'lstat', lstat):
                    with self.assertRaisesRegex(ValueError, 'Reparse build input'):
                        BUILD.source_rows(root)

    def test_fresh_lan_package_uses_its_receipt_and_still_rejects_tampering(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, cache, private = root / 'source', root / 'cache', root / 'private'
            self.source_fixture(source)
            ordinary, lan = cache / 'builds/v8-p5', cache / 'builds/v8-p5-lan'
            self.bound_build(ordinary, source, 'ordinary-guid')
            self.bound_build(lan, source, 'fresh-lan-guid')
            package = cache / 'packages/fresh-lan'
            shutil.copytree(lan / 'DungeonContract', package)
            self.manifest(package, [path.relative_to(package).as_posix() for path in package.rglob('*') if path.is_file()])
            (lan / 'player-package.json').write_text(json.dumps({'path': str(package)}), encoding='utf-8')
            private.mkdir()
            for name in PACKAGE.SECRET_NAMES:
                (private / name).write_text('fixture-private-value-' + name, encoding='ascii')
            with patch.multiple(PACKAGE, ROOT=source, CACHE=cache, PRIVATE=private), patch.object(
                    PACKAGE, 'validate_build', side_effect=lambda build: BUILD.validate_build(build, source)):
                with contextlib.redirect_stdout(io.StringIO()):
                    PACKAGE.main()
                report = json.loads((source / 'docs/game-demo/v8-package-validation.json').read_text(encoding='utf-8'))
                self.assertEqual(report['status'], 'PASS_OFFLINE_CARRIER_INTEGRITY_ONLY')
                target = package / 'DungeonContract_Data/boot.config'
                target.write_bytes(b'tampered')
                self.manifest(package, [path.relative_to(package).as_posix() for path in package.rglob('*')
                                        if path.is_file() and path.name != 'package-manifest.json'])
                with self.assertRaisesRegex(ValueError, 'measured client build mismatch'):
                    PACKAGE.main()

    def test_repackaged_lan_requires_the_current_ordinary_parent(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, ordinary, lan, package = (root / name for name in ('source', 'ordinary', 'lan', 'package'))
            self.source_fixture(source)
            self.bound_build(ordinary, source, 'ordinary-guid')
            shutil.copytree(ordinary, lan)
            shutil.copytree(lan / 'DungeonContract', package)
            receipt_path = lan / 'v8-build-receipt.json'
            receipt = json.loads(receipt_path.read_text(encoding='utf-8'))
            receipt.update(unity_build_source='verified_ordinary_parent_repackage',
                           parent_receipt_sha256=PACKAGE.digest(ordinary / 'v8-build-receipt.json'))
            receipt_path.write_text(json.dumps(receipt), encoding='utf-8')
            PACKAGE.verify_package_build(package, ordinary, lan)
            receipt['parent_receipt_sha256'] = '0' * 64
            receipt_path.write_text(json.dumps(receipt), encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'parent receipt mismatch'):
                PACKAGE.verify_package_build(package, ordinary, lan)

    def test_package_rejects_unlisted_private_names_and_noncanonical_paths(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / 'readme.md').write_text('player', encoding='utf-8')
            self.manifest(root, ['readme.md'])
            PACKAGE.verify_inventory(root)
            (root / 'jwt-secret.txt').write_text('private', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'unlisted'):
                PACKAGE.verify_inventory(root)
            self.manifest(root, ['readme.md', 'jwt-secret.txt'])
            with self.assertRaisesRegex(ValueError, 'server/private'):
                PACKAGE.verify_inventory(root)
            (root / 'jwt-secret.txt').unlink()
            self.manifest(root, ['readme.md'])
            value = PACKAGE.read(root / 'package-manifest.json')
            value['files'][0]['path'] = './readme.md'
            (root / 'package-manifest.json').write_text(json.dumps(value), encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'invalid manifest'):
                PACKAGE.verify_inventory(root)

    def test_package_scans_manifest_and_cross_chunk_credentials(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            private = root / 'private'
            private.mkdir()
            secret = b'a' * 64
            for name in PACKAGE.SECRET_NAMES:
                (private / name).write_bytes(secret)
            payload = root / 'player.bin'
            payload.write_bytes(b'.' * (1024 * 1024 - 30) + secret + b'.')
            with self.assertRaisesRegex(ValueError, 'private credential'):
                PACKAGE.scan_private([payload], private)
            with self.assertRaises(ValueError):
                PACKAGE.require(False, 'validation remains enabled')

    def test_package_client_native_artifact_must_match_verified_build(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            build, package = root / 'build', root / 'player'
            package.mkdir()
            (package / 'UnityPlayer.dll').write_bytes(b'engine')
            row = {'path': 'DungeonContract/UnityPlayer.dll', 'bytes': 6, 'sha256': PACKAGE.digest(package / 'UnityPlayer.dll')}
            with patch.object(PACKAGE, 'artifacts', return_value=[row]):
                PACKAGE.verify_client_build(package, build)
                (package / 'UnityPlayer.dll').write_bytes(b'changed')
                with self.assertRaisesRegex(ValueError, 'measured client build mismatch'):
                    PACKAGE.verify_client_build(package, build)

    def test_integration_preflight_reports_resource_and_docker_blocks(self):
        script = r"""
$ErrorActionPreference='Stop'
$global:portfolioPreflightDockerCalls=0
function global:Get-CimInstance {
    if($env:PORTFOLIO_TEST_BLOCK -eq 'memory'){return [pscustomobject]@{FreeVirtualMemory=1024;FreePhysicalMemory=1024}}
    return [pscustomobject]@{FreeVirtualMemory=8GB/1KB;FreePhysicalMemory=8GB/1KB}
}
function global:docker {
    if($args[0] -ne 'version'){throw 'Preflight attempted a Docker mutation.'}
    $global:portfolioPreflightDockerCalls++
    $global:LASTEXITCODE=1
}
$failed=$false
try{& (Join-Path $env:PORTFOLIO_CI_SCRIPT_ROOT 'portfolio-ci-dc.ps1') -ReportPath $env:PORTFOLIO_TEST_REPORT}catch{$failed=$true}
if(!$failed){throw 'Blocked preflight returned success.'}
if($env:PORTFOLIO_TEST_BLOCK -eq 'memory' -and $global:portfolioPreflightDockerCalls -ne 0){throw 'Low memory preflight reached Docker.'}
if($env:PORTFOLIO_TEST_BLOCK -eq 'engine' -and $global:portfolioPreflightDockerCalls -ne 1){throw 'Offline preflight did not stop after version.'}
"""
        for block, expected in (('memory', 'ENV_RESOURCE_BLOCKED'), ('engine', 'ENV_DOCKER_BLOCKED')):
            if block == 'memory' and os.name != 'nt':
                continue  # The Windows memory preflight is exercised by the Windows tooling job.
            with self.subTest(block=block), tempfile.TemporaryDirectory() as temporary:
                report = Path(temporary) / 'preflight.json'
                environment = dict(os.environ, PORTFOLIO_CI_SCRIPT_ROOT=str(Path(__file__).resolve().parent),
                                   PORTFOLIO_TEST_BLOCK=block, PORTFOLIO_TEST_REPORT=str(report))
                result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], env=environment, capture_output=True, text=True, timeout=20)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                value = json.loads(report.read_text(encoding='utf-8'))
                self.assertEqual(value['status'], expected)
                self.assertEqual(value['phase'], 'PREFLIGHT')
                self.assertEqual(value['cleanup'], 'NOT_NEEDED')
                self.assertEqual(value['passed_tests'], [])

    def test_integration_failure_keeps_bounded_redacted_diagnostics_before_scoped_cleanup(self):
        script = r'''
$ErrorActionPreference='Stop'
$global:portfolioFakeProject=''
$global:portfolioFakeEnvFile=''
$global:portfolioFakeSecrets=@()
$global:portfolioFakeCalls=[Collections.Generic.List[string]]::new()
function global:Get-CimInstance { [pscustomobject]@{FreeVirtualMemory=8GB/1KB;FreePhysicalMemory=8GB/1KB} }
function global:docker {
    $arguments=@($args)
    $global:LASTEXITCODE=0
    if($arguments[0] -eq 'version'){return 'fixture-engine'}
    if($arguments[0] -eq 'compose'){
        $projectIndex=[Array]::IndexOf($arguments,'--project-name')
        $envIndex=[Array]::IndexOf($arguments,'--env-file')
        if($projectIndex -lt 0 -or $envIndex -lt 0){throw 'Unscoped Compose operation.'}
        $projectName=$arguments[$projectIndex+1]
        if($projectName -notmatch '^portfolio-dc-ci-[a-f0-9]{10}$'){throw 'Unexpected Compose namespace.'}
        if($global:portfolioFakeProject -and $global:portfolioFakeProject -ne $projectName){throw 'Compose namespace changed.'}
        $global:portfolioFakeProject=$projectName
        $global:portfolioFakeEnvFile=$arguments[$envIndex+1]
        if($arguments -contains 'config'){
            $global:portfolioFakeCalls.Add('config')
            $values=@{}
            foreach($line in [IO.File]::ReadAllLines($global:portfolioFakeEnvFile)){
                $parts=$line.Split('=',2);$values[$parts[0]]=$parts[1]
            }
            $global:portfolioFakeSecrets=@($values.GetEnumerator()|Where-Object Key -NE 'PORTFOLIO_PROJECT_ROOT'|ForEach-Object Value)
            return (@{name=$projectName;services=@{mysql=@{image='fixture'};redis=@{image='fixture'};runner=@{image='fixture'}};volumes=@{mysql=@{name=$projectName+'_mysql'}}}|ConvertTo-Json -Depth 5 -Compress)
        }
        if($arguments -contains 'up'){
            $global:portfolioFakeCalls.Add('up')
            Write-Output ('redis unhealthy '+$global:portfolioFakeSecrets[0])
            $global:LASTEXITCODE=if($env:PORTFOLIO_TEST_DIAGNOSTIC_MODE -eq 'go_failure'){0}else{23}
            return
        }
        if($arguments -contains 'exec' -and $env:PORTFOLIO_TEST_DIAGNOSTIC_MODE -eq 'go_failure'){
            if($arguments -contains './cmd/tools/v2_migrate'){$global:portfolioFakeCalls.Add('migration');return 'fixture migration'}
            if($arguments -contains 'test'){
                $global:portfolioFakeCalls.Add('go-test')
                Write-Output (@{Action='fail';Package='fixture/gameplay';Test=('TestV8Fixture/'+$global:portfolioFakeSecrets[0])}|ConvertTo-Json -Compress)
                $global:LASTEXITCODE=41;return
            }
        }
        if($arguments -contains 'down'){
            if($arguments -notcontains '--volumes' -or $arguments -notcontains '--remove-orphans'){throw 'Incomplete isolated cleanup.'}
            if($env:PORTFOLIO_TEST_DIAGNOSTIC_MODE -ne 'lookup_failure' -and $global:portfolioFakeCalls -notcontains 'inspect:222222222222'){throw 'Cleanup ran before Redis health capture.'}
            $global:portfolioFakeCalls.Add('down')
            Write-Output ('scoped cleanup '+$global:portfolioFakeSecrets[1])
            return
        }
        throw 'Go/exec or unexpected Compose action ran after startup failure.'
    }
    if($arguments[0] -eq 'ps'){
        if($arguments -notcontains ('label=com.docker.compose.project='+$global:portfolioFakeProject) -or $arguments -notcontains '{{.ID}}'){throw 'Container lookup lacks exact project filter.'}
        $global:portfolioFakeCalls.Add('ps')
        if($env:PORTFOLIO_TEST_DIAGNOSTIC_MODE -eq 'lookup_failure'){
            Write-Output ('lookup failed '+$global:portfolioFakeSecrets[2]);$global:LASTEXITCODE=19;return
        }
        return @('111111111111','222222222222','333333333333')
    }
    if($arguments[0] -eq 'inspect'){
        $formatIndex=[Array]::IndexOf($arguments,'--format')
        if($formatIndex -lt 0){throw 'Full container inspect was requested.'}
        $format=$arguments[$formatIndex+1]
        if($format -match '\.Config\.Env|\.Args|\.Path|\.Config\.Cmd|\.Config\.Healthcheck|json \.\}\}'){throw 'Unsafe diagnostic inspect fields.'}
        $id=$arguments[-1]
        $global:portfolioFakeCalls.Add('inspect:'+$id)
        if($id -eq '333333333333'){
            return (@{project='dc-demo';service='redis';status='running';running=$true;exit_code=0;oom_killed=$false;error='';health=$null}|ConvertTo-Json -Compress)
        }
        $service=if($id -eq '222222222222'){'redis'}else{'mysql'}
        $health=if($service -eq 'redis'){@{Status='unhealthy';FailingStreak=3;Log=@(@{Start='fixture-start';End='fixture-end';ExitCode=1;Output=('redis-cli health failure '+($global:portfolioFakeSecrets -join ' '))})}}else{$null}
        return (@{project=$global:portfolioFakeProject;service=$service;status='running';running=$true;exit_code=0;oom_killed=$false;error=('fixture-state '+$global:portfolioFakeSecrets[3]);health=$health}|ConvertTo-Json -Depth 6 -Compress)
    }
    if($arguments[0] -eq 'logs'){
        $id=$arguments[-1]
        if($id -eq '333333333333'){throw 'Read logs belonging to the original dc-demo project.'}
        if($arguments -notcontains '--tail' -or $arguments -notcontains '40'){throw 'Unbounded container logs requested.'}
        $global:portfolioFakeCalls.Add('logs:'+$id)
        if($id -eq '222222222222' -and $env:PORTFOLIO_TEST_DIAGNOSTIC_MODE -eq 'log_failure'){
            Write-Output ('fixture log request failed '+$global:portfolioFakeSecrets[4]);$global:LASTEXITCODE=31;return
        }
        Write-Output (('.'*16370)+$global:portfolioFakeSecrets[0])
        foreach($index in 1..100){Write-Output ('fixture log line '+$index+' '+$global:portfolioFakeSecrets[4])}
        return
    }
    throw 'Unexpected Docker command.'
}
$failed=$false
$captured=@(try{& (Join-Path $env:PORTFOLIO_CI_SCRIPT_ROOT 'portfolio-ci-dc.ps1') -ReportPath $env:PORTFOLIO_TEST_REPORT *>&1}catch{$failed=$true;Write-Output $_.Exception.Message})
if(!$failed){throw 'Failed startup was incorrectly accepted.'}
$reportText=[IO.File]::ReadAllText($env:PORTFOLIO_TEST_REPORT)
$allText=($captured -join [Environment]::NewLine)+$reportText
foreach($secret in $global:portfolioFakeSecrets){
    if($allText.Contains($secret) -or $allText.Contains($secret.Substring(0,16))){throw 'Diagnostic output exposed a credential or truncated prefix.'}
}
if(Test-Path -LiteralPath $global:portfolioFakeEnvFile){throw 'Temporary credential file survived cleanup.'}
if($global:portfolioFakeCalls[-1] -ne 'down'){throw 'Isolated cleanup did not run last.'}
Write-Output (@{fake_docker=$true;failed=$failed;credential_matches=0;calls=@($global:portfolioFakeCalls);project=$global:portfolioFakeProject}|ConvertTo-Json -Compress)
'''
        for mode in ('unhealthy', 'log_failure', 'lookup_failure', 'go_failure'):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as temporary:
                report = Path(temporary) / 'failure.json'
                environment = dict(os.environ, PORTFOLIO_CI_SCRIPT_ROOT=str(Path(__file__).resolve().parent),
                                   PORTFOLIO_TEST_REPORT=str(report), PORTFOLIO_TEST_DIAGNOSTIC_MODE=mode)
                result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], env=environment,
                                        capture_output=True, text=True, timeout=25)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertFalse(re.search(r'\b[a-fA-F0-9]{64}\b', result.stdout + result.stderr))
                value = json.loads(report.read_text(encoding='utf-8'))
                self.assertEqual(value['status'], 'FAIL')
                self.assertEqual(value['phase'], 'INTEGRATION')
                expected_operation, expected_exit = ('GO_TEST', 41) if mode == 'go_failure' else ('COMPOSE_UP', 23)
                self.assertEqual(value['operation'], expected_operation)
                self.assertEqual(value['command_exit_code'], expected_exit)
                self.assertEqual(value['cleanup'], 'PASS')
                self.assertEqual(value['passed_tests'], [])
                diagnostics = value['diagnostics']
                self.assertTrue(diagnostics['captured_before_cleanup'])
                self.assertEqual(diagnostics['command_exit_code'], expected_exit)
                if mode == 'go_failure':
                    self.assertIn('TestV8Fixture/[REDACTED]', value['failures'])
                if mode == 'lookup_failure':
                    self.assertTrue(any('exit 19' in error for error in diagnostics['errors']))
                    continue
                redis = next(row for row in diagnostics['containers'] if row['service'] == 'redis')
                self.assertEqual(redis['health']['status'], 'unhealthy')
                self.assertEqual(redis['health']['checks'][0]['exit_code'], 1)
                self.assertIn('redis-cli health failure', redis['health']['checks'][0]['output'])
                self.assertIn('[REDACTED]', redis['health']['checks'][0]['output'])
                runner = next(row for row in diagnostics['containers'] if row['service'] == 'runner')
                self.assertEqual(runner['status'], 'NOT_OBSERVED')
                self.assertTrue(any('labels do not match' in error for error in diagnostics['errors']))
                self.assertLessEqual(len(redis['log_tail']), 40)
                self.assertLessEqual(sum(map(len, redis['log_tail'])), 16384)
                if mode == 'log_failure':
                    self.assertEqual(redis['log_exit_code'], 31)
                else:
                    self.assertTrue(redis['log_truncated'])

    def test_source_audit_records_missing_history_and_private_scope(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            docs = root / 'docs/game-demo'
            docs.mkdir(parents=True)
            (root / 'README.md').write_text('source', encoding='utf-8')
            (root / 'resource.txt').write_text('legal', encoding='utf-8')
            (docs / 'source-resources.json').write_text(json.dumps({'schema_version': 1, 'required_paths': ['resource.txt']}), encoding='utf-8')
            report = root / 'audit.json'
            with patch.multiple(SOURCE, ROOT=root, DOCS=docs, PRIVATE=root / 'absent-private'), patch('sys.argv', ['audit', '--report', str(report)]), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(SystemExit) as result:
                    SOURCE.main()
            self.assertEqual(result.exception.code, 0)
            value = json.loads(report.read_text(encoding='utf-8'))
            self.assertEqual(value['historical_hash_comparison'], 'NOT_AVAILABLE_IN_CURATED_SOURCE')
            self.assertEqual(value['private_literal_scan'], 'NOT_ASSESSED_NO_PRIVATE_CREDENTIALS')
            self.assertEqual(value['protected_checks'], 0)


if __name__ == '__main__':
    unittest.main()
