"""Cheap source-tooling checks; no Go compilation, Docker starts or Unity."""
from pathlib import Path
import ast
import json
import os
import subprocess
import sys
import unittest

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]


def run(arguments, **options):
    result = subprocess.run(arguments, cwd=ROOT, check=False, **options)
    if result.returncode:
        raise SystemExit(result.returncode)


def main():
    files = list((ROOT / 'deploy').glob('*.py'))
    for path in files:
        ast.parse(path.read_text(encoding='utf-8-sig'), filename=str(path))
    for name in ('test_dc_paths.py', 'test_portfolio_export.py', 'test_tooling_audits.py'):
        if not (ROOT / 'deploy' / name).is_file():
            raise SystemExit('Required tooling suite is missing: ' + name)
    tests = unittest.TestSuite()
    for name in ('test_dc_paths.py', 'test_portfolio_export.py', 'test_tooling_audits.py'):
        suite = unittest.defaultTestLoader.discover(str(ROOT / 'deploy'), pattern=name)
        if not suite.countTestCases():
            raise SystemExit('Required tooling suite selected no tests: ' + name)
        tests.addTests(suite)
    result = unittest.TextTestRunner(verbosity=2).run(tests)
    if not result.wasSuccessful() or result.skipped:
        raise SystemExit('Tooling checks failed or skipped required tests')
    script = r'''
$ErrorActionPreference='Stop'
$files=@(Get-ChildItem -LiteralPath (Join-Path $env:DC_TOOLING_CHECK_ROOT 'deploy') -Filter '*.ps1')
foreach($file in $files){
    $tokens=$null;$errors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    if($errors.Count){$errors|ForEach-Object Message;exit 1}
}
Write-Output ('PowerShell files parsed: '+$files.Count)
'''
    run(['pwsh', '-NoProfile', '-Command', script], env=dict(os.environ, DC_TOOLING_CHECK_ROOT=str(ROOT)))
    if os.name == 'nt':
        run(['pwsh', '-NoProfile', '-File', str(ROOT / 'deploy/test_portfolio.ps1')])
    print(json.dumps({'status': 'PASS_CHEAP_TOOLING_CHECKS', 'python_files_parsed': len(files),
                      'passed_tests': result.testsRun, 'skipped_tests': len(result.skipped),
                      'windows_acl_model': 'CHECKED' if os.name == 'nt' else 'WINDOWS_JOB_REQUIRED',
                      'go_built': False, 'docker_started': False, 'unity_started': False}))


if __name__ == '__main__':
    main()
