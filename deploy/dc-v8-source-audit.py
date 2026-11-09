"""Low-memory source/history audit. Does not claim build/game/performance acceptance."""
from pathlib import Path
import ast
import hashlib
import json
import os
import re
import sys
import argparse
from dc_paths import project_root, private_root
from dc_v8_build_identity import resource_paths

sys.dont_write_bytecode = True
ROOT = project_root()
DOCS = ROOT / 'docs/game-demo'
PRIVATE = private_root(ROOT)
BLOCKED = {'library', 'temp', 'logs', 'usersettings', 'obj', 'bin', '.git', 'node_modules', '.vs', '__pycache__', 'private'}


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for data in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(data)
    return h.hexdigest()


def files():
    for folder, directories, names in os.walk(ROOT):
        directories[:] = [name for name in directories if name.lower() not in BLOCKED]
        for name in names:
            if name.startswith('.env') or 'handoff' in name.lower() or '交接' in name:
                continue
            yield Path(folder) / name


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--report', type=Path, default=DOCS / 'v8-source-audit-results.json')
    args = parser.parse_args()
    errors = []
    # Historical preimages remain authoritative when present. Curated source
    # exports deliberately omit them; validate the current resource inventory and
    # explicitly record that no historical hash comparison was possible.
    baseline = DOCS / 'v8-implementation-preimage.json'
    before = json.loads(baseline.read_text(encoding='utf-8-sig')) if baseline.is_file() else None
    current_resources = resource_paths(ROOT)
    protected = list(before['protected_resources']) if before else []
    protected += [row for row in before['files'] if row['path'].endswith('.sql') or row['path'].startswith('docs/game-demo/config/')
                  or row['path'].startswith('docs/game-demo/contracts/') and row['path'].endswith('.json')
                  or row['path'].startswith('docs/game-demo/v7-') and row['path'].endswith('.json')] if before else []
    for row in protected:
        path = ROOT / row['path']
        if not path.is_file() or digest(path) != row['sha256']:
            errors.append('protected:' + row['path'])
    links = 0
    for path in list(DOCS.glob('*.md')) + list((DOCS / 'contracts/runtime-v8').glob('*.md')) + [ROOT / 'README.md']:
        for target in re.findall(r'(?<!!)\[[^\]]+\]\(([^)]+)\)', path.read_text(encoding='utf-8-sig')):
            target = target.split('#', 1)[0].strip('<>')
            if not target or re.match(r'^[a-z]+:', target, re.I):
                continue
            links += 1
            if not (path.parent / target).exists():
                errors.append('link:' + path.relative_to(ROOT).as_posix() + ':' + target)
    python_count = 0
    for path in (ROOT / 'deploy').glob('*.py'):
        try:
            ast.parse(path.read_text(encoding='utf-8-sig'), filename=str(path))
            python_count += 1
        except SyntaxError:
            errors.append('python_syntax:' + path.name)
    guids = {}
    meta_count = 0
    for path in (ROOT / 'client/unity-demo/Assets').rglob('*.meta'):
        content = path.read_text(encoding='utf-8-sig')
        match = re.search(r'(?m)^guid: ([a-f0-9]{32})\s*$', content)
        if not match or not path.with_suffix('').exists():
            errors.append('orphan_or_invalid_meta:' + str(path.relative_to(ROOT)))
            continue
        guid = match.group(1)
        if guid in guids:
            errors.append('duplicate_guid:' + str(path.relative_to(ROOT)))
        guids[guid] = path
        meta_count += 1
    secrets = []
    for name in ('root-password.txt', 'db-password.txt', 'redis-password.txt', 'jwt-secret.txt', 'service-token.txt', 'admission-key.txt', 'metrics-token.txt'):
        path = PRIVATE / name
        if path.is_file():
            value = path.read_bytes().strip()
            if value:
                secrets += [value, value.decode('ascii').encode('utf-16-le')]
    scanned = 0
    overlap = max(map(len, secrets), default=1) - 1
    for path in files():
        scanned += 1
        tail = b''
        with path.open('rb') as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                data = tail + chunk
                if any(value in data for value in secrets):
                    errors.append('private_literal:' + path.relative_to(ROOT).as_posix())
                    break
                tail = data[-overlap:] if overlap else b''
    result = {'profile': 'dc_ruins.v8.p5.1', 'status': 'PASS_SOURCE_ONLY_AUDIT' if not errors else 'FAIL_SOURCE_ONLY_AUDIT',
              'scope': 'history/resources/source syntax/local links/private literals; no build or real-Run acceptance',
              'protected_resources': len(current_resources), 'protected_checks': len(protected), 'local_links': links,
              'historical_hash_comparison': 'CHECKED' if before else 'NOT_AVAILABLE_IN_CURATED_SOURCE',
              'private_literal_scan': 'CHECKED_AVAILABLE_CREDENTIALS' if secrets else 'NOT_ASSESSED_NO_PRIVATE_CREDENTIALS',
              'python_files_parsed': python_count, 'unity_meta_files_checked': meta_count, 'eligible_source_files_scanned': scanned,
              'private_literal_matches': sum(error.startswith('private_literal:') for error in errors), 'errors': errors, 'p5_complete': False}
    args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))
    raise SystemExit(1 if errors else 0)


if __name__ == '__main__':
    main()
