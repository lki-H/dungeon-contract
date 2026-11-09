"""Create a separate, allowlisted source tree; never publish or modify the source."""
from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
import shutil
import stat
import subprocess
import tempfile
from pathlib import Path, PurePosixPath


ROOT = Path(__file__).resolve().parents[1]
POLICY = Path("docs/portfolio/public-export-manifest.json")
FORBIDDEN_PARTS = {".git", "node_modules", "library", "temp", "obj", "bin", "logs", "private", "evidence", "__pycache__"}
FORBIDDEN_SUFFIXES = (".exe", ".dll", ".pdb", ".pyc", ".pem", ".key", ".sql.gz", ".dump", ".vhd", ".vhdx", ".log", ".tmp", ".db", ".sqlite", ".sqlite3", ".bak")
FORBIDDEN_NAMES = {"root-password.txt", "db-password.txt", "redis-password.txt", "jwt-secret.txt",
                   "service-token.txt", "admission-key.txt", "metrics-token.txt", "credentials.json", "secrets.json"}


def read(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def reparse(path: Path) -> bool:
    info = path.lstat()
    return path.is_symlink() or bool(getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT)


def relative_name(value: str) -> str:
    name = PurePosixPath(value)
    if not value or value == '.' or name.as_posix() != value or "\\" in value or name.is_absolute() or any(part in ("", "..") for part in name.parts) or ":" in value:
        raise ValueError("Unsafe policy path: " + value)
    return name.as_posix()


def excluded(name: str, patterns: list[str]) -> bool:
    path = PurePosixPath(name)
    return (
        any(part.lower() in FORBIDDEN_PARTS for part in path.parts)
        or path.name.lower() in FORBIDDEN_NAMES
        or path.name.lower().startswith(".env") and path.name != ".env.example"
        or name.lower().endswith(FORBIDDEN_SUFFIXES)
        or any(fnmatch.fnmatchcase(name, pattern) or path.match(pattern) for pattern in patterns)
    )


def selected_files(root: Path, policy: dict) -> list[Path]:
    selected = set()
    for raw in policy["include"]:
        directory = raw.endswith("/")
        name = relative_name(raw.rstrip("/"))
        target = root / name
        if not target.exists():
            raise ValueError("Required public input is missing: " + name)
        cursor = target
        while cursor != root:
            if reparse(cursor):
                raise ValueError("Reparse input is not exported: " + name)
            cursor = cursor.parent
        if directory:
            if not target.is_dir():
                raise ValueError("Expected directory: " + name)
            for base, directories, filenames in os.walk(target, followlinks=False):
                permitted = []
                for child in directories:
                    candidate = Path(base, child)
                    relative = candidate.relative_to(root).as_posix()
                    if excluded(relative + "/", policy["exclude"]):
                        continue
                    if reparse(candidate):
                        raise ValueError("Reparse input is not exported: " + relative)
                    permitted.append(child)
                directories[:] = permitted
                for filename in filenames:
                    candidate = Path(base, filename)
                    if not excluded(candidate.relative_to(root).as_posix(), policy["exclude"]):
                        if reparse(candidate):
                            raise ValueError("Reparse input is not exported: " + str(candidate))
                        selected.add(candidate)
        elif not excluded(name, policy["exclude"]):
            if not target.is_file():
                raise ValueError("Expected file: " + name)
            selected.add(target)
    return sorted(selected)


def checkout_line_endings(root: Path, paths: list[Path]) -> dict[str, str]:
    """Use Git's real attribute rules in an isolated, data-free temporary repo."""
    attributes = [path for path in paths if path.name == '.gitattributes']
    if not attributes:
        return {}
    environment = {key: value for key, value in os.environ.items() if not key.startswith('GIT_')}
    environment.update(GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL=os.devnull)
    git = ['git', '-c', 'core.autocrlf=false', '-c', 'core.attributesFile=' + os.devnull]
    names = [path.relative_to(root).as_posix() for path in paths]
    queried = ('text', 'eol', 'working-tree-encoding', 'filter', 'ident')
    with tempfile.TemporaryDirectory(prefix='dc-export-attributes-') as temporary:
        isolated = Path(temporary)
        try:
            result = subprocess.run([*git, 'init', '--quiet', '--template=', str(isolated)],
                                    env=environment, capture_output=True, timeout=20)
            if result.returncode:
                raise ValueError('Unable to create isolated Git attribute reader')
            for source in attributes:
                target = isolated / source.relative_to(root)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, target)
            result = subprocess.run([*git, 'check-attr', '-z', '--stdin', *queried],
                                    cwd=isolated, env=environment, input=('\0'.join(names) + '\0').encode('utf-8'),
                                    capture_output=True, timeout=20)
            if result.returncode:
                raise ValueError('Unable to read the exported Git attribute policy')
        except (OSError, subprocess.TimeoutExpired) as error:
            raise ValueError('Attribute-aware source export requires a working local Git command') from error
    fields = result.stdout.decode('utf-8').split('\0')
    if fields[-1] != '' or len(fields) != len(names) * len(queried) * 3 + 1:
        raise ValueError('Unexpected Git attribute response')
    values = {name: {} for name in names}
    for index in range(0, len(fields) - 1, 3):
        name, attribute, value = fields[index:index + 3]
        if name not in values or attribute not in queried or attribute in values[name]:
            raise ValueError('Unexpected Git attribute entry')
        values[name][attribute] = value
    endings = {}
    for name, attributes in values.items():
        for attribute in ('working-tree-encoding', 'filter', 'ident'):
            if attributes[attribute] not in ('unspecified', 'unset'):
                raise ValueError('Unsupported checkout transformation ' + attribute + ': ' + name)
        text, eol = attributes['text'], attributes['eol']
        if text == 'unset':
            continue
        if text not in ('set', 'unspecified'):
            raise ValueError('Export requires explicit text/eol attributes: ' + name)
        if eol in ('lf', 'crlf'):
            endings[name] = eol
        elif text == 'set':
            raise ValueError('Text export input requires an explicit checkout eol: ' + name)
    return endings


def exported_bytes(source: Path, ending: str | None) -> bytes:
    data = source.read_bytes()
    if ending is None:
        return data
    if b'\0' in data:
        raise ValueError('Declared text export input contains NUL bytes: ' + source.name)
    data = data.replace(b'\r\n', b'\n')
    return data.replace(b'\n', b'\r\n') if ending == 'crlf' else data


def export(root: Path, output: Path) -> dict:
    root = root.resolve(strict=True)
    output = output.absolute()
    if output.exists() or output.is_symlink():
        raise ValueError("Destination must not exist; existing files are never overwritten")
    output = output.parent.resolve(strict=True) / output.name
    if output == root or root in output.parents or output in root.parents:
        raise ValueError("Destination must be separate from the working source")
    policy = read(root / POLICY)
    paths = selected_files(root, policy)
    endings = checkout_line_endings(root, paths)
    output.mkdir()
    rows = []
    normalized = []
    for source in paths:
        name = source.relative_to(root).as_posix()
        destination = output / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        content = exported_bytes(source, endings.get(name))
        destination.write_bytes(content)
        if hashlib.sha256(content).hexdigest() != sha(destination):
            raise ValueError("Copy verification failed: " + name)
        if sha(source) != sha(destination):
            normalized.append(name)
        rows.append({"path": name, "bytes": destination.stat().st_size, "sha256": sha(destination)})
    manifest = {
        "schema_version": 1,
        "kind": "allowlisted_public_source_snapshot",
        "profile": "dc_ruins.v8.p5.1",
        "code_license": "MIT",
        "ai_backdrop_included": False,
        "source_history_included": False,
        "git_initialized": False,
        "published": False,
        "policy_sha256": sha(output / POLICY) if (output / POLICY).is_file() else sha(root / POLICY),
        "source_policy_sha256": sha(root / POLICY),
        "line_endings": {"policy": ".gitattributes", "normalized_files": normalized, "source_files_modified": False},
        "files": rows,
    }
    with (output / "source-export.json").open('w', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--list", action="store_true")
    args = parser.parse_args()
    if args.list:
        root = args.root.resolve(strict=True)
        paths = selected_files(root, read(root / POLICY))
        print(json.dumps({"files": [p.relative_to(root).as_posix() for p in paths]}, ensure_ascii=False))
        return 0
    if args.output is None:
        parser.error("--output is required unless --list is used")
    result = export(args.root, args.output)
    print(json.dumps({"status": "EXPORTED_SOURCE_ONLY", "files": len(result["files"]), "destination": str(args.output), "published": False}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
