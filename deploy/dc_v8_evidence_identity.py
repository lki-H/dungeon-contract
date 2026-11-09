"""Bind reports to the current measured binaries, not just a reusable profile name."""
from functools import lru_cache
from pathlib import Path
import hashlib
import json
import os
from dc_v8_build_identity import ROOT, CACHE, validate_build

BUILD = CACHE / 'builds/v8-p5'
PROFILE = 'dc_ruins.v8.p5.1'
RUNTIME = {
    'control': 'dc-control.exe',
    'authority_managed': 'GameService/GameService_Data/Managed/Assembly-CSharp.dll',
    'client_managed': 'DungeonContract/DungeonContract_Data/Managed/Assembly-CSharp.dll',
}
STANDALONE = (
    'DungeonContract/DungeonContract.exe',
    RUNTIME['client_managed'], RUNTIME['authority_managed'],
    'DungeonContract/Content/demo-v8-p5.json',
    'DungeonContract/Content/parameters-v8-p5.json',
    'DungeonContract/Content/world-v8-p5.json',
)


def canonical(path):
    return os.path.normcase(os.path.abspath(path))


@lru_cache(maxsize=16)
def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def validate_rows(document, expected, require_roles=False):
    """Fail closed on missing, duplicate, foreign-path or mismatched fingerprint rows."""
    failures = []
    if document.get('profile') != PROFILE:
        failures.append('profile')
    rows = document.get('files')
    if not isinstance(rows, list):
        return failures + ['files_missing']
    seen = set()
    for row in rows:
        if not isinstance(row, dict) or not isinstance(row.get('path'), str):
            failures.append('invalid_row')
            continue
        path = canonical(row['path'])
        if path in seen:
            failures.append('duplicate_path')
        seen.add(path)
        wanted = expected.get(path)
        if wanted is None:
            failures.append('unexpected_path')
            continue
        if row.get('sha256') != wanted['sha256']:
            failures.append('binary_hash:' + wanted['label'])
        if require_roles and row.get('role') != wanted['role']:
            failures.append('role:' + wanted['label'])
    if seen != set(expected):
        failures.append('incomplete_binary_set')
    return failures


def binding(manifest, kind='runtime'):
    manifest = Path(manifest)
    if not manifest.is_file():
        return {'matches_current_binary_set': False, 'status': 'MISSING_BINARY_MANIFEST'}
    result = {'reference': {'path': str(manifest), 'sha256': digest(str(manifest))}}
    try:
        document = json.loads(manifest.read_text(encoding='utf-8-sig'))
        expected = {}
        paths = RUNTIME.items() if kind == 'runtime' else ((None, p) for p in STANDALONE)
        for role, relative in paths:
            path = BUILD / relative
            expected[canonical(path)] = {'sha256': digest(str(path)), 'role': role, 'label': relative}
        failures = validate_rows(document, expected, require_roles=kind == 'runtime')
        failures += current_build_failures()
        if kind != 'runtime':
            freeze = ROOT / 'docs/game-demo/v8-runtime-source-freeze.json'
            if document.get('runtime_source_freeze_sha256') != digest(str(freeze)):
                failures.append('runtime_source_freeze')
    except (OSError, ValueError, TypeError, AttributeError):
        failures = ['unreadable_or_invalid_binding']
    result.update(matches_current_binary_set=not failures,
                  status='CURRENT_BINARY_SET' if not failures else 'HISTORICAL_OR_UNBOUND_BINARY_SET',
                  mismatches=failures)
    return result


@lru_cache(maxsize=1)
def current_build_failures():
    return ['build_receipt:' + error for error in validate_build(BUILD)]


def completed_memory(path):
    try:
        value = json.loads(Path(path).read_text(encoding='utf-8-sig'))
        return value.get('profile') == PROFILE and value.get('status') == 'completed' and value.get('exit_code') == 0 and bool(value.get('samples'))
    except (OSError, ValueError, AttributeError):
        return False
