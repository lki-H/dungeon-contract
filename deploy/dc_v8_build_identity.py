"""Build receipts bind successful ordinary builds to their actual source inputs."""
from pathlib import Path, PurePosixPath
import argparse
import datetime
import hashlib
import json
import os
import stat
from dc_paths import cache_root, project_root

ROOT = project_root()
CACHE = cache_root(ROOT)
PROFILE = 'dc_ruins.v8.p5.1'
CONFIGS = ('demo-v8-p5.json', 'parameters-v8-p5.json', 'world-v8-p5.json')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    with path.open('w', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def archive(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def resource_paths(root):
    root = Path(root).resolve()
    inventory_path = checked_input(root, root / 'docs/game-demo/source-resources.json')
    inventory = read(inventory_path)
    if inventory.get('schema_version') != 1:
        raise ValueError('Unsupported current resource inventory')
    resources = {inventory_path}
    seen = set()
    required = inventory.get('required_paths')
    optional = inventory.get('optional_components', {})
    if not isinstance(required, list) or not isinstance(optional, dict):
        raise ValueError('Invalid current resource inventory')
    for names in [required] + list(optional.values()):
        if not isinstance(names, list) or not names or any(not isinstance(name, str) for name in names):
            raise ValueError('Invalid resource component')
        paths = []
        for relative in names:
            name = PurePosixPath(relative)
            if not relative or '\\' in relative or ':' in relative or name.is_absolute() or '..' in name.parts or name.as_posix() != relative:
                raise ValueError('Invalid or duplicated current resource: ' + relative)
            path = checked_input(root, root / relative).resolve()
            if not path.is_relative_to(root.resolve()) or path in seen:
                raise ValueError('Invalid or duplicated current resource: ' + relative)
            seen.add(path)
            paths.append(path)
        present = [path.is_file() for path in paths]
        if names is required and not all(present):
            raise ValueError('Missing required current resource')
        if names is not required and any(present) and not all(present):
            raise ValueError('Partial optional resource component')
        if all(present):
            resources.update(paths)
    return resources


def checked_input(root, path):
    """Reject links before resolving them, including links to the same source tree."""
    path = Path(path)
    if not path.is_relative_to(root):
        raise ValueError('Build input escapes source root: ' + str(path))
    cursor = path
    while cursor != root:
        try:
            attributes = getattr(cursor.lstat(), 'st_file_attributes', 0)
        except FileNotFoundError:
            cursor = cursor.parent
            continue
        if cursor.is_symlink() or attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError('Reparse build input: ' + cursor.relative_to(root).as_posix())
        cursor = cursor.parent
    return path


def input_files(root, relative, ignored_directories=()):
    folder = root / relative
    checked_input(root, folder)
    if not folder.exists():
        return
    for base, directories, names in os.walk(folder, followlinks=False):
        directories[:] = [name for name in directories if name.lower() not in ignored_directories]
        for name in directories + names:
            checked_input(root, Path(base, name))
        for name in names:
            yield Path(base, name)


def preserve_build(build):
    parent = (CACHE / 'builds').resolve(strict=True)
    if build.parent.resolve() != parent or build.name not in ('v8-p5', 'v8-p5-lan') or build.is_symlink():
        raise ValueError('Build preservation path boundary')
    if not build.exists():
        return None
    if not build.is_dir() or build.resolve().parent != parent:
        raise ValueError('Build preservation directory boundary')
    target = parent / (build.name + '-before-completion-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
    if target.exists():
        raise ValueError('Build preservation target already exists')
    build.rename(target)
    return str(target)


def source_rows(root=ROOT):
    root = Path(root).resolve()
    # Unity compiles every script under Assets, including the shared control
    # client and Editor/importer code. Meta files and other assets affect the
    # shipped player as well; Library and imported caches are not source inputs.
    files = set(input_files(root, 'client/unity-demo/Assets'))
    files.update(input_files(root, 'client/unity-demo/ProjectSettings'))
    backend_caches = {'__pycache__', '.git', 'node_modules', 'private', 'evidence', 'logs', 'temp', 'obj', 'bin'}
    for folder in ('backend/internal', 'backend/cmd/server', 'backend/cmd/tools/dc_lan_gateway', 'backend/cmd/tools/dc_lan_player'):
        # go:embed can compile non-Go files (currently task rules*.json) into
        # the executable. Keep all source-folder inputs, including future embed
        # types, while excluding tests and generated cache/log files.
        files.update(p for p in input_files(root, folder, backend_caches)
                     if not p.name.endswith('_test.go') and p.suffix.lower() not in ('.pyc', '.log', '.tmp', '.bak'))
    for relative in ('backend/go.mod', 'backend/go.sum', 'client/unity-demo/Packages/manifest.json',
                     'client/unity-demo/Packages/packages-lock.json', 'client/unity-demo/Assets/DungeonContract/Editor/DcBuild.cs'):
        files.add(root / relative)
    files.update(root / 'docs/game-demo/config' / n for n in CONFIGS)
    # Go loads these definitions from disk at startup, independently of its exe.
    for folder in ('docs/game-demo/config', 'docs/game-demo/contracts'):
        files.update(p for p in input_files(root, folder) if p.suffix == '.json')
    files.update(resource_paths(root))
    for path in files:
        checked_input(root, path)
    return [{'path': p.relative_to(root).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)} for p in sorted(files)]


def compare_rows(recorded, current):
    if not isinstance(recorded, list) or not recorded:
        return ['missing_source_or_artifact_rows']
    expected = {r['path']: r for r in current}
    seen = set()
    errors = []
    for row in recorded:
        if not isinstance(row, dict) or not isinstance(row.get('path'), str):
            errors.append('invalid_row')
            continue
        name = row['path']
        if name in seen:
            errors.append('duplicate:' + name)
        seen.add(name)
        wanted = expected.get(name)
        if wanted is None or any(row.get(k) != wanted[k] for k in ('bytes', 'sha256')):
            errors.append('changed:' + name)
    if seen != set(expected):
        errors.append('file_set_changed')
    return errors


def artifacts(build):
    build = Path(build).resolve()
    names = ['dc-control.exe', 'DungeonContract/DungeonContract.exe',
             'GameService/GameService.exe',
             'GameService/GameService_Data/Managed/Assembly-CSharp.dll',
             'DungeonContract/DungeonContract_Data/Managed/Assembly-CSharp.dll']
    # LAN roles are compiled from project source too. Once included in a receipt,
    # removal or replacement must invalidate it just like the authority/client DLLs.
    names += [n for n in ('dc-lan-gateway.exe', 'dc-lan-player.exe') if (build / n).is_file()]
    names += [f'{role}/Content/{name}' for role in ('GameService', 'DungeonContract') for name in CONFIGS]
    # Unity's executable, native libraries and player data all participate in the
    # running build. Binding only Assembly-CSharp.dll leaves those replaceable.
    for role in ('GameService', 'DungeonContract'):
        names += [p.relative_to(build).as_posix() for p in (build / role).rglob('*')
                  if p.is_file() and not any('donotship' in part.lower() for part in p.relative_to(build / role).parts)]
    for name in names:
        if not (build / name).resolve().is_relative_to(build):
            raise ValueError('Build artifact escapes build directory: ' + name)
    return [{'path': n, 'bytes': (build / n).stat().st_size, 'sha256': sha(build / n)} for n in sorted(set(names))]


def validate_build(build, root=ROOT):
    try:
        value = read(build / 'v8-build-receipt.json')
        errors = [] if value.get('profile') == PROFILE and value.get('status') == 'SUCCESSFUL_BUILD_BOUND_TO_INPUTS' else ['receipt_identity']
        errors += compare_rows(value.get('sources'), source_rows(root))
        current_artifacts = artifacts(build)
        errors += compare_rows(value.get('artifacts'), current_artifacts)
        recorded_artifacts = value.get('artifacts')
        if isinstance(recorded_artifacts, list):
            recorded_names = {row.get('path') for row in recorded_artifacts if isinstance(row, dict)}
            if recorded_names != {row['path'] for row in current_artifacts}:
                errors.append('receipt_artifact_inventory_incomplete_or_changed')
        for role in ('GameService', 'DungeonContract'):
            for name in CONFIGS:
                if sha(build / role / 'Content' / name) != sha(root / 'docs/game-demo/config' / name):
                    errors.append('bundled_config:' + role + ':' + name)
        return errors
    except (OSError, ValueError, KeyError, TypeError, AttributeError):
        return ['missing_or_invalid_build_receipt']


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=('begin', 'complete', 'verify'))
    parser.add_argument('build_root')
    parser.add_argument('--parent')
    args = parser.parse_args()
    build = Path(args.build_root).resolve()
    if build not in ((CACHE / 'builds/v8-p5').resolve(), (CACHE / 'builds/v8-p5-lan').resolve()):
        raise SystemExit('Unsupported v8 build directory')
    pending = build / 'v8-build-inputs.json'
    if args.action == 'verify':
        errors = validate_build(build)
        print(json.dumps({'status': 'PASS_CURRENT_SOURCES_AND_BUILD' if not errors else 'FAIL_BUILD_BINDING', 'failures': errors}))
        raise SystemExit(1 if errors else 0)
    if args.action == 'begin':
        initial={'profile': PROFILE, 'started_at': datetime.datetime.now().astimezone().isoformat(), 'sources': source_rows()}
        archived=CACHE/'evidence'/('v8-build-inputs-'+build.name+'-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f')+'.json')
        archive(archived,initial)
        initial['input_evidence']={'path':str(archived),'sha256':sha(archived)}
        # Both v8 PowerShell build entries call begin after their memory admission.
        # A rejected admission must leave the old build at its original path.
        initial['previous_build_preserved'] = preserve_build(build)
        build.mkdir(parents=True, exist_ok=True)
        write(pending, initial)
        print(json.dumps({'status': 'BUILD_INPUTS_CAPTURED', 'previous_build_preserved': initial['previous_build_preserved']}))
        return
    initial = read(pending)
    if initial.get('profile') != PROFILE or compare_rows(initial.get('sources'), source_rows()):
        raise SystemExit('Sources changed during build; no receipt was issued')
    parent = None
    if args.parent:
        parent = Path(args.parent).resolve()
        if parent != (CACHE / 'builds/v8-p5').resolve() or validate_build(parent):
            raise SystemExit('Repackaging requires a valid ordinary build with current source inputs')
        for role in ('GameService', 'DungeonContract'):
            relative = f'{role}/{role}_Data/Managed/Assembly-CSharp.dll'
            if sha(build / relative) != sha(parent / relative):
                raise SystemExit('Repackaged managed assembly differs from verified parent')
    receipt = {'schema_version': 2, 'artifact_scope': 'all_shipped_role_files_and_control_tools',
               'profile': PROFILE, 'status': 'SUCCESSFUL_BUILD_BOUND_TO_INPUTS', 'started_at': initial['started_at'],
               'completed_at': datetime.datetime.now().astimezone().isoformat(),
               'unity_build_source': 'verified_ordinary_parent_repackage' if parent else 'ordinary_Unity_build',
               'sources': initial['sources'], 'artifacts': artifacts(build)}
    if parent:
        receipt['parent_receipt_sha256'] = sha(parent / 'v8-build-receipt.json')
    if initial.get('input_evidence'):
        receipt['input_evidence']=initial['input_evidence']
    for role in ('GameService', 'DungeonContract'):
        for name in CONFIGS:
            if sha(build / role / 'Content' / name) != sha(ROOT / 'docs/game-demo/config' / name):
                raise SystemExit('Bundled configuration does not match source')
    evidence = CACHE / 'evidence' / ('v8-build-receipt-' + build.name + '-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f') + '.json')
    archive(evidence, receipt)
    write(build / 'v8-build-receipt.json', receipt)
    if build == (CACHE / 'builds/v8-p5').resolve():
        runtime = [row for row in receipt['sources'] if row['path'].startswith('client/unity-demo/Assets/DungeonContract/Runtime/')]
        write(ROOT / 'docs/game-demo/v8-runtime-source-freeze.json', {'profile': PROFILE, 'recorded_at': receipt['completed_at'],
              'scope': 'successful_ordinary_build_bound_sources_and_artifacts', 'build_receipt': str(evidence),
              'build_receipt_sha256': sha(evidence), 'files': runtime})
        write(ROOT / 'docs/game-demo/v8-current-normal-build.json', {'profile': PROFILE, 'status': receipt['status'],
              'receipt': str(evidence), 'receipt_sha256': sha(evidence), 'build_root': str(build)})
    print(json.dumps({'status': receipt['status'], 'source_inputs': len(receipt['sources']), 'receipt': str(evidence)}))


if __name__ == '__main__':
    main()
