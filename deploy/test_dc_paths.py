from pathlib import Path
import json
import hashlib
import os
import tempfile
import unittest

from dc_paths import cache_root, private_root, project_root
from dc_v8_build_identity import artifacts, compare_rows, resource_paths, source_rows


class PortablePathTests(unittest.TestCase):
    def create_source(self, directory):
        root = Path(directory) / 'source with spaces'
        (root / 'backend').mkdir(parents=True)
        (root / 'client/unity-demo').mkdir(parents=True)
        return root

    def expected_default(self, root, directory):
        value = str(root.resolve()).rstrip('/\\')
        if os.name == 'nt':
            value = value.lower()
        identity = hashlib.sha256(value.encode('utf-8')).hexdigest()[:12]
        return (Path(directory) / 'DungeonContract/projects' / identity / 'cache').resolve()

    def test_root_follows_script_without_external_workspace(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = self.create_source(temporary)
            self.assertEqual(project_root({}, root), root.resolve())

    def test_explicit_source_and_cache_are_independent(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = self.create_source(temporary)
            values = {'DC_PROJECT_ROOT': str(root), 'DC_CACHE_ROOT': str(Path(temporary) / 'cache with spaces')}
            self.assertEqual(project_root(values), root.resolve())
            self.assertEqual(cache_root(root, values), Path(values['DC_CACHE_ROOT']).resolve())

    def test_private_override_does_not_change_cache(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = self.create_source(temporary)
            values = {'LOCALAPPDATA': temporary, 'DC_PRIVATE_ROOT': str(Path(temporary) / 'separate private')}
            self.assertEqual(private_root(root, values), Path(values['DC_PRIVATE_ROOT']).resolve())
            self.assertEqual(cache_root(root, values), self.expected_default(root, temporary))

    def test_nonlegacy_source_has_user_local_default(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = self.create_source(temporary)
            self.assertEqual(cache_root(root, {'LOCALAPPDATA': temporary}), self.expected_default(root, temporary))

    def test_separate_checkouts_receive_separate_defaults(self):
        with tempfile.TemporaryDirectory() as temporary:
            first = self.create_source(temporary)
            second = Path(temporary) / 'second source'
            second.mkdir()
            values = {'LOCALAPPDATA': temporary}
            self.assertNotEqual(cache_root(first, values), cache_root(second, values))

    def test_incomplete_root_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            with self.assertRaises(ValueError):
                project_root({'DC_PROJECT_ROOT': temporary})

    def test_cache_and_private_cannot_overlap_source(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = self.create_source(temporary)
            for target in (root, root / 'cache', root.parent):
                with self.subTest(target=target):
                    with self.assertRaises(ValueError):
                        cache_root(root, {'DC_CACHE_ROOT': str(target)})
                    with self.assertRaises(ValueError):
                        private_root(root, {'DC_PRIVATE_ROOT': str(target)})

    def test_full_player_artifacts_bind_native_engine_and_data(self):
        with tempfile.TemporaryDirectory() as temporary:
            build = Path(temporary)
            names = ['dc-control.exe']
            for role in ('GameService', 'DungeonContract'):
                names += [f'{role}/{role}.exe', f'{role}/UnityPlayer.dll',
                          f'{role}/{role}_Data/Managed/Assembly-CSharp.dll', f'{role}/{role}_Data/data.bin']
                names += [f'{role}/Content/{name}-v8-p5.json' for name in ('demo', 'parameters', 'world')]
            for name in names:
                target = build / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(name.encode('utf-8'))
            recorded = artifacts(build)
            for name in ('GameService/GameService.exe', 'DungeonContract/UnityPlayer.dll', 'DungeonContract/DungeonContract_Data/data.bin'):
                with self.subTest(name=name):
                    target = build / name
                    original = target.read_bytes()
                    target.write_bytes(b'changed')
                    self.assertIn('changed:' + name, compare_rows(recorded, artifacts(build)))
                    target.write_bytes(original)

    def test_inventory_does_not_require_historical_preimage(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = self.create_source(temporary)
            paths = ('backend/go.mod', 'backend/go.sum', 'client/unity-demo/Packages/manifest.json',
                     'client/unity-demo/Packages/packages-lock.json', 'client/unity-demo/Assets/DungeonContract/Editor/DcBuild.cs')
            for relative in paths:
                target = root / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text('{}', encoding='utf-8')
            configs = root / 'docs/game-demo/config'
            configs.mkdir(parents=True)
            for name in ('demo-v8-p5.json', 'parameters-v8-p5.json', 'world-v8-p5.json'):
                (configs / name).write_text('{}', encoding='utf-8')
            resource = root / 'client/unity-demo/Assets/Resources/legal.txt'
            resource.parent.mkdir(parents=True)
            resource.write_text('legal', encoding='utf-8')
            inventory = root / 'docs/game-demo/source-resources.json'
            inventory.write_text(json.dumps({'schema_version': 1, 'required_paths': [resource.relative_to(root).as_posix()], 'optional_components': {}}), encoding='utf-8')
            rows = source_rows(root)
            self.assertIn(resource.relative_to(root).as_posix(), {row['path'] for row in rows})
            self.assertFalse((root / 'docs/game-demo/v8-implementation-preimage.json').exists())

    def test_partial_optional_component_and_escape_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = self.create_source(temporary)
            inventory = root / 'docs/game-demo/source-resources.json'
            inventory.parent.mkdir(parents=True)
            present = root / 'optional.txt'
            present.write_text('source', encoding='utf-8')
            required = root / 'required.txt'
            required.write_text('required', encoding='utf-8')
            inventory.write_text(json.dumps({'schema_version': 1, 'required_paths': ['required.txt'], 'optional_components': {'private': ['optional.txt', 'missing.txt']}}), encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'Partial optional'):
                source_rows(root)
            inventory.write_text(json.dumps({'schema_version': 1, 'required_paths': ['../outside.txt'], 'optional_components': {}}), encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'Invalid or duplicated'):
                source_rows(root)

    def test_complete_optional_component_may_be_excluded(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = self.create_source(temporary)
            inventory = root / 'docs/game-demo/source-resources.json'
            inventory.parent.mkdir(parents=True)
            required = root / 'required.txt'
            required.write_text('required', encoding='utf-8')
            inventory.write_text(json.dumps({'schema_version': 1, 'required_paths': ['required.txt'], 'optional_components': {'private': ['private.png', 'private.png.meta']}}), encoding='utf-8')
            self.assertEqual(resource_paths(root), {required.resolve(), inventory})
            for name in ('private.png', 'private.png.meta'):
                (root / name).write_text('optional', encoding='utf-8')
            self.assertEqual(len(resource_paths(root)), 4)

    def test_missing_required_and_duplicate_component_reject(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = self.create_source(temporary)
            inventory = root / 'docs/game-demo/source-resources.json'
            inventory.parent.mkdir(parents=True)
            inventory.write_text(json.dumps({'schema_version': 1, 'required_paths': ['missing.txt']}), encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'Missing required'):
                resource_paths(root)
            present = root / 'present.txt'
            present.write_text('required', encoding='utf-8')
            inventory.write_text(json.dumps({'schema_version': 1, 'required_paths': ['present.txt'], 'optional_components': {'bad': ['present.txt']}}), encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'Invalid or duplicated'):
                resource_paths(root)


if __name__ == '__main__':
    unittest.main()
