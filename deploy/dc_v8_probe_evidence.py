"""Preserve rotating probe aliases and bind each successful result to source inputs."""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import time

from dc_v8_build_identity import ROOT, CACHE, PROFILE, compare_rows, source_rows, validate_build

EVIDENCE = CACHE / 'evidence'
KINDS = ('native', 'compatibility', 'ui-ranger', 'ui-runesmith')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def create(path, data):
    with path.open('xb') as stream:
        stream.write(data)


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def probe_sources(kind):
    rows = {r['path']: r for r in source_rows()}
    if kind in ('native', 'compatibility'):
        editor = ROOT / 'client/unity-demo/Assets/DungeonContract/Editor'
        for path in list(editor.glob('*.cs')) + list((editor / 'Fixtures').glob('*.json')):
            relative = path.relative_to(ROOT).as_posix()
            rows[relative] = {'path': relative, 'bytes': path.stat().st_size, 'sha256': sha(path)}
    return [rows[k] for k in sorted(rows)]


def aliases():
    patterns = ('v8-combat-visual-*.json', 'v8-native-state-samples.json', 'v8-wave-defect-reproduction.json', 'v8-model-checks.json', 'v8-regression-p1.json', '*-model-checks.json',
                'v8-attachment-*.json', 'v8-p5-ui-*.png', 'v8-p5-ui-probe*.json', 'v8-probe-latest-*.json', 'v8-probe-attempt-*.json')
    return sorted({p for pattern in patterns for p in EVIDENCE.glob(pattern) if p.is_file()})


def result_path(kind):
    return EVIDENCE / ('v8-model-checks.json' if kind == 'native' else 'v8-regression-p1.json' if kind == 'compatibility' else 'v8-attachment-' + kind[3:] + '.json')


def validate_probe(kind):
    try:
        pointer = read(EVIDENCE / ('v8-probe-latest-' + kind + '.json'))
        receipt = Path(pointer['receipt']).resolve()
        if receipt.parent.parent != EVIDENCE.resolve() or not receipt.parent.name.startswith('v8-probe-') or receipt.name != 'receipt.json' or sha(receipt) != pointer['sha256']:
            raise ValueError('receipt boundary/fingerprint')
        value = read(receipt)
        attempt = read(EVIDENCE / ('v8-probe-attempt-' + kind + '.json'))
        errors = [] if value.get('profile') == PROFILE and value.get('kind') == kind and value.get('status') == 'SUCCESSFUL_SOURCE_BOUND_PROBE' else ['probe_identity']
        if Path(attempt['directory']).resolve() != receipt.parent:
            errors.append('latest_probe_attempt_not_complete')
        errors += compare_rows(value.get('sources'), probe_sources(kind))
        result = receipt.parent / 'result.json'
        if sha(result) != value['result']['sha256']:
            errors.append('result_fingerprint')
        for artifact in value.get('artifacts', []):
            target=(receipt.parent/artifact['path']).resolve()
            if not target.is_relative_to(receipt.parent) or sha(target)!=artifact['sha256']:
                errors.append('artifact_fingerprint')
        if kind.startswith('ui-'):
            from dc_v8_evidence_identity import binding
            bound = binding(value['binary_manifest'], 'standalone')
            if sha(Path(value['binary_manifest'])) != value.get('binary_manifest_sha256') or not bound['matches_current_binary_set']:
                errors.append('probe_binary_binding')
        return {'matches_current_sources': not errors, 'receipt': str(receipt), 'receipt_sha256': sha(receipt),
                'result': {'path': str(result), 'sha256': sha(result)}, 'failures': errors}
    except (OSError, ValueError, KeyError, TypeError, AttributeError):
        return {'matches_current_sources': False, 'failures': ['missing_or_invalid_source_bound_probe']}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=('begin', 'complete'))
    parser.add_argument('--output', required=True)
    parser.add_argument('--kind', choices=KINDS)
    parser.add_argument('--binary-manifest')
    args = parser.parse_args()
    output = Path(args.output).resolve()
    if output.parent != EVIDENCE.resolve() or not output.name.startswith('v8-probe-'):
        raise SystemExit('Probe output must be a new dedicated Demo evidence folder')
    if args.action == 'begin':
        if args.kind is None:
            raise SystemExit('Probe kind required')
        output.mkdir(exist_ok=False)
        initial = {'profile': PROFILE, 'kind': args.kind, 'started_epoch_ns': time.time_ns(),
                   'sources': probe_sources(args.kind), 'aliases_preserved': []}
        backup = output / 'before'
        backup.mkdir()
        for path in aliases():
            data = path.read_bytes()
            if len(data) > 16777216:
                raise SystemExit('Probe alias exceeds archive bound')
            create(backup / path.name, data)
            initial['aliases_preserved'].append({'path': path.name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
        create(output / 'inputs.json', encoded(initial))
        temporary = output / 'attempt.tmp'
        create(temporary, encoded({'directory': str(output), 'inputs_sha256': sha(output / 'inputs.json')}))
        temporary.replace(EVIDENCE / ('v8-probe-attempt-' + args.kind + '.json'))
        print('Preserved rotating aliases and captured probe inputs: ' + str(output))
        return
    initial = read(output / 'inputs.json')
    kind = initial['kind']
    if initial.get('profile') != PROFILE or kind not in KINDS or compare_rows(initial.get('sources'), probe_sources(kind)):
        raise SystemExit('Probe source inputs changed; result not admitted')
    result = result_path(kind)
    if result.stat().st_mtime_ns < initial['started_epoch_ns']:
        raise SystemExit('Probe did not write a fresh result')
    value = read(result)
    legacy_ok=kind=='compatibility' and value.get('kind')=='v3_p1_authoritative_simulation' and isinstance(value.get('passed_groups'),int) and value['passed_groups']>0 and len(value.get('cases',[]))==value['passed_groups']
    if not legacy_ok and (value.get('status') != 'PASS' or value.get('profile') != PROFILE):
        raise SystemExit('Probe result identity/status does not match')
    if kind.startswith('ui-'):
        from dc_v8_evidence_identity import binding
        manifest = Path(args.binary_manifest or '').resolve()
        if manifest.parent != EVIDENCE.resolve() or not manifest.name.startswith('v8-uicheck-binaries-'):
            raise SystemExit('UI probe requires its launch binary manifest')
        launch = read(manifest)
        if launch.get('loadout') != kind[3:] or not binding(manifest, 'standalone')['matches_current_binary_set']:
            raise SystemExit('UI probe binary/source identity mismatch')
    data = result.read_bytes()
    create(output / 'result.json', data)
    receipt = {'profile': PROFILE, 'kind': kind, 'status': 'SUCCESSFUL_SOURCE_BOUND_PROBE',
               'recorded_at': datetime.datetime.now().astimezone().isoformat(), 'sources': initial['sources'],
               'result': {'path': 'result.json', 'sha256': hashlib.sha256(data).hexdigest()}, 'artifacts': []}
    if kind.startswith('ui-'):
        receipt['binary_manifest'] = str(manifest)
        receipt['binary_manifest_sha256'] = sha(manifest)
        fresh = output / 'after'
        fresh.mkdir()
        for path in aliases():
            if path.stat().st_mtime_ns >= initial['started_epoch_ns']:
                data = path.read_bytes()
                create(fresh / path.name, data)
                receipt['artifacts'].append({'path': 'after/' + path.name, 'sha256': hashlib.sha256(data).hexdigest()})
    create(output / 'receipt.json', encoded(receipt))
    pointer = EVIDENCE / ('v8-probe-latest-' + kind + '.json')
    temporary = output / 'pointer.tmp'
    create(temporary, encoded({'receipt': str(output / 'receipt.json'), 'sha256': sha(output / 'receipt.json')}))
    temporary.replace(pointer)
    print('Recorded successful source-bound probe: ' + str(output))


if __name__ == '__main__':
    main()
