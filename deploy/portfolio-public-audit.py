"""Audit selected public source, or every file in an exact exported source tree."""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
import re
from pathlib import Path
from urllib.parse import unquote


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('portfolio_export', Path(__file__).with_name('portfolio-export.py'))
EXPORT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXPORT)
SECRET_NAMES = {'root-password.txt', 'db-password.txt', 'redis-password.txt', 'jwt-secret.txt',
                'service-token.txt', 'admission-key.txt', 'metrics-token.txt', 'credentials.json', 'secrets.json'}
SECRET_PATTERNS = (
    re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
    re.compile(r'''(?i)(?:api[_-]?key|access[_-]?token|github_pat|password)\s*[:=]\s*['"]?(?P<value>[A-Za-z0-9_+/=-]{24,})'''),
)
PUBLIC_PLACEHOLDERS = {'replace-with-a-local-password', 'replace-with-a-local-admin-password', 'caller-value-not-a-secret'}
ABSOLUTE_PATH = re.compile(r'(?i)(?:[A-Z]:\\|/[a-zA-Z0-9_.-]+/Users/|/home/[a-zA-Z0-9_.-]+/)')


def exact_files(root):
    def fail(error):
        raise error
    for base, directories, names in os.walk(root, followlinks=False, onerror=fail):
        for name in directories + names:
            path = Path(base, name)
            if EXPORT.reparse(path):
                raise ValueError('Reparse input is not audited: ' + path.relative_to(root).as_posix())
        for name in names:
            yield Path(base, name)


def audit(root, exact=False):
    failures, warnings, rows = [], [], []
    try:
        root = Path(root).resolve(strict=True)
        if not root.is_dir():
            raise ValueError('Public source root must be a directory')
        files = sorted(exact_files(root) if exact else EXPORT.selected_files(root, EXPORT.read(root / EXPORT.POLICY)))
    except (OSError, ValueError, KeyError, TypeError) as error:
        return {'schema_version': 2, 'status': 'BLOCKED', 'scope': 'exact_export' if exact else 'policy_selected_source',
                'files': [], 'failures': [{'kind': 'invalid_source_tree', 'detail': str(error)}], 'warnings': []}
    for path in files:
        relative = path.relative_to(root).as_posix()
        if EXPORT.excluded(relative, []) or path.name.lower() in SECRET_NAMES:
            failures.append({'kind': 'forbidden_file', 'path': relative})
        try:
            content = path.read_text(encoding='utf-8-sig')
        except UnicodeDecodeError:
            content = ''
        if any(match.groupdict().get('value') not in PUBLIC_PLACEHOLDERS
               for pattern in SECRET_PATTERNS for match in pattern.finditer(content)):
            failures.append({'kind': 'secret_like_text', 'path': relative})
        if ABSOLUTE_PATH.search(content) and relative not in {'deploy/dc-common.ps1', 'deploy/portfolio.ps1'}:
            warnings.append({'kind': 'absolute_path_reference', 'path': relative})
        if path.name.lower() == 'ruins-lobby-ai.png':
            failures.append({'kind': 'excluded_ai_asset_in_public_source', 'path': relative})
        if exact and path.suffix.lower() == '.md':
            for target in re.findall(r'!?\[[^\]]*\]\(([^)]+)\)', content):
                target = unquote(target.split('#', 1)[0].strip('<>'))
                if not target or re.match(r'^(?!file:)[a-z][a-z0-9+.-]*:', target, re.I):
                    continue
                linked = (root / target.lstrip('/')) if target.startswith('/') else path.parent / target
                if not linked.resolve().is_relative_to(root) or not linked.exists():
                    failures.append({'kind': 'broken_or_escaping_link', 'path': relative, 'target': target})
        rows.append({'path': relative, 'bytes': path.stat().st_size, 'sha256': EXPORT.sha(path)})
    snapshot = root / 'source-export.json'
    if exact and snapshot.is_file():
        try:
            recorded = EXPORT.read(snapshot)['files']
            actual = {row['path']: row for row in rows if row['path'] != 'source-export.json'}
            indexed = {}
            for row in recorded:
                name = EXPORT.relative_name(row['path'])
                if name in indexed or row != actual.get(name):
                    raise ValueError('Changed or duplicated export entry: ' + name)
                indexed[name] = row
            if set(indexed) != set(actual):
                raise ValueError('Unlisted or missing export files')
        except (OSError, ValueError, KeyError, TypeError) as error:
            failures.append({'kind': 'invalid_export_manifest', 'detail': str(error)})
    return {'schema_version': 2, 'status': 'BLOCKED' if failures else 'PASS_WITH_REVIEW',
            'scope': 'exact_export' if exact else 'policy_selected_source', 'files': rows,
            'failures': failures, 'warnings': warnings, 'published': False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, help='Audit every file in this exact exported source tree')
    parser.add_argument('--json', dest='json_path', type=Path)
    args = parser.parse_args()
    result = audit(args.root or ROOT, exact=args.root is not None)
    rendered = json.dumps(result, ensure_ascii=False, indent=2) + '\n'
    if args.json_path:
        args.json_path.write_text(rendered, encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('status', 'scope', 'failures', 'warnings')}, ensure_ascii=False))
    return 1 if result['status'] == 'BLOCKED' else 0


if __name__ == '__main__':
    raise SystemExit(main())
