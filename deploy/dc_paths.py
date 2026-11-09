"""Shared paths for current Demo build and verification tools."""
from pathlib import Path
import hashlib
import os


def external_path(path, root, label):
    path = Path(path).expanduser().resolve()
    root = Path(root).resolve()
    if path == root or path.is_relative_to(root) or root.is_relative_to(path):
        raise ValueError(label + ' must be separate from the source tree')
    return path


def project_root(environment=None, script_root=None):
    values = os.environ if environment is None else environment
    fallback = Path(__file__).resolve().parents[1] if script_root is None else Path(script_root)
    root = Path(values.get('DC_PROJECT_ROOT') or fallback).expanduser().resolve()
    if not (root / 'backend').is_dir() or not (root / 'client/unity-demo').is_dir():
        raise ValueError('DC_PROJECT_ROOT must name a complete Demo source directory')
    return root


def cache_root(root=None, environment=None):
    values = os.environ if environment is None else environment
    root = project_root(values) if root is None else Path(root).resolve()
    configured = values.get('DC_CACHE_ROOT')
    if configured:
        return external_path(configured, root, 'DC_CACHE_ROOT')
    legacy_root = Path('E:/game-realtime-gm-demo').resolve()
    legacy_cache = Path('D:/dev-cache/dungeon-contract')
    if os.name == 'nt' and root == legacy_root and (legacy_cache / 'private').is_dir():
        return legacy_cache.resolve()
    local = values.get('LOCALAPPDATA')
    base = Path(local) if local else Path.home() / '.cache'
    identity_source = str(root).rstrip('/\\')
    if os.name == 'nt':
        identity_source = identity_source.lower()
    identity = hashlib.sha256(identity_source.encode('utf-8')).hexdigest()[:12]
    return (base / 'DungeonContract/projects' / identity / 'cache').expanduser().resolve()


def private_root(root=None, environment=None):
    values = os.environ if environment is None else environment
    root = project_root(values) if root is None else Path(root).resolve()
    configured = values.get('DC_PRIVATE_ROOT')
    return external_path(configured or cache_root(root, values) / 'private', root, 'DC_PRIVATE_ROOT')
