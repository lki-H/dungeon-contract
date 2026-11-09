"""Verify a portable carrier without Docker or claiming gameplay acceptance."""
from pathlib import Path, PurePosixPath
import datetime
import hashlib
import json
import os
import re
import stat
from urllib.parse import unquote
from dc_paths import project_root, cache_root, private_root
from dc_v8_build_identity import artifacts, validate_build

ROOT = project_root()
CACHE = cache_root(ROOT)
PRIVATE = private_root(ROOT)
SECRET_NAMES = ('root-password.txt', 'db-password.txt', 'redis-password.txt', 'jwt-secret.txt',
                'service-token.txt', 'admission-key.txt', 'metrics-token.txt')


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(chunk)
    return value.hexdigest()


def require(condition, message):
    # These are validation checks, so Python -O must never remove them.
    if not condition:
        raise ValueError(message)


def package_files(package):
    files = []
    for base, directories, names in os.walk(package, followlinks=False):
        for name in directories + names:
            path = Path(base, name)
            attributes = getattr(path.lstat(), 'st_file_attributes', 0)
            require(not path.is_symlink() and not attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT, 'reparse path in package')
        files += [Path(base, name) for name in names]
    return files


def verify_inventory(package):
    package = Path(package).resolve(strict=True)
    manifest = package / 'package-manifest.json'
    value = read(manifest)
    require(value.get('profile') == 'dc_ruins.v8.p5.1', 'package profile mismatch')
    indexed = set()
    for row in value['files']:
        name = row['path']
        relative = PurePosixPath(name)
        require(bool(name) and '\\' not in name and ':' not in name and not relative.is_absolute()
                and '..' not in relative.parts and relative.as_posix() == name, 'invalid manifest path')
        path = package / name
        require(path.resolve().is_relative_to(package), 'manifest path escapes package')
        require(name not in indexed and name != manifest.name, 'duplicate or recursive manifest entry')
        indexed.add(name)
        require(path.stat().st_size == row['bytes'] and digest(path) == row['sha256'], 'package file mismatch: ' + name)
    files = package_files(package)
    require({path.relative_to(package).as_posix() for path in files if path != manifest} == indexed, 'unlisted or missing files')
    forbidden = {name.lower() for name in SECRET_NAMES} | {'gameservice.exe', 'dc-control.exe', 'dc-lan-gateway.exe', 'redis.conf'}
    require(not any(path.name.lower() in forbidden or path.name.lower().startswith('.env') for path in files), 'server/private file in player carrier')
    return value, files, indexed


def scan_private(files, private):
    secrets = [(private / name).read_bytes().strip() for name in SECRET_NAMES]
    require(all(secrets), 'empty private credential prevents package audit')
    secrets += [value.decode('ascii').encode('utf-16-le') for value in secrets]
    overlap = max(map(len, secrets)) - 1
    for path in files:
        tail = b''
        with path.open('rb') as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                data = tail + chunk
                require(not any(secret in data for secret in secrets), 'private credential literal in: ' + path.name)
                tail = data[-overlap:] if overlap else b''


def verify_client_build(package, build):
    for row in artifacts(build):
        if row['path'].startswith('DungeonContract/'):
            relative = row['path'].removeprefix('DungeonContract/')
            path = package / relative
            require(path.is_file() and path.stat().st_size == row['bytes'] and digest(path) == row['sha256'],
                    'measured client build mismatch: ' + relative)


def verify_package_build(package, ordinary_build, lan_build):
    # A freshly compiled LAN role has its own Unity build GUID and player data.
    # Its receipt, rather than an independent ordinary build, owns those files.
    verify_client_build(package, lan_build)
    receipt = read(lan_build / 'v8-build-receipt.json')
    source = receipt.get('unity_build_source')
    require(source in ('ordinary_Unity_build', 'verified_ordinary_parent_repackage'), 'unsupported LAN build source')
    if source == 'verified_ordinary_parent_repackage':
        require(receipt.get('parent_receipt_sha256') == digest(ordinary_build / 'v8-build-receipt.json'),
                'repackaged ordinary parent receipt mismatch')
        verify_client_build(package, ordinary_build)


def main():
    require(not validate_build(CACHE / 'builds/v8-p5'), 'ordinary build does not match current sources')
    require(not validate_build(CACHE / 'builds/v8-p5-lan'), 'LAN host build does not match current sources')
    package = Path(read(CACHE / 'builds/v8-p5-lan/player-package.json')['path']).resolve()
    require(package != (CACHE / 'packages').resolve() and package.is_relative_to((CACHE / 'packages').resolve()), 'package boundary')
    value, files, indexed = verify_inventory(package)
    scan_private(files, PRIVATE)
    verify_package_build(package, CACHE / 'builds/v8-p5', CACHE / 'builds/v8-p5-lan')
    local_links = 0
    for path in package.rglob('*.md'):
        for target in re.findall(r'(?<!!)\[[^\]]+\]\(([^)]+)\)', path.read_text(encoding='utf-8-sig')):
            target = unquote(target.split('#', 1)[0].strip('<>'))
            if not target or re.match(r'^https?://|^mailto:', target, re.I):
                continue
            linked = (path.parent / target).resolve()
            require(linked.is_relative_to(package) and linked.exists(), 'broken or escaping package link: ' + path.name)
            local_links += 1
    report = {'profile': value['profile'], 'recorded_at': datetime.datetime.now().astimezone().isoformat(),
              'status': 'PASS_OFFLINE_CARRIER_INTEGRITY_ONLY', 'package': str(package),
              'manifest_sha256': digest(package / 'package-manifest.json'), 'indexed_files': len(indexed), 'local_links': local_links,
              'private_literal_matches': 0, 'credential_encodings_checked': ['ascii', 'utf16le'], 'measured_client_binary_matches': True,
              'player_build_source': value.get('player_build_source'),
              'network_two_run_acceptance': 'NOT_ASSESSED_BY_OFFLINE_AUDIT', 'human': False, 'cross_pc': False}
    (ROOT / 'docs/game-demo/v8-package-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: report[key] for key in ('status', 'indexed_files', 'local_links', 'private_literal_matches')}))


if __name__ == '__main__':
    main()
