import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile


SPEC = importlib.util.spec_from_file_location("portfolio_export", Path(__file__).with_name("portfolio-export.py"))
EXPORT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXPORT)
AUDIT_SPEC = importlib.util.spec_from_file_location('portfolio_public_audit', Path(__file__).with_name('portfolio-public-audit.py'))
AUDIT = importlib.util.module_from_spec(AUDIT_SPEC)
AUDIT_SPEC.loader.exec_module(AUDIT)


class PublicExportTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="dc-public-export-")
        self.base = Path(self.temporary.name)
        self.root = self.base / "source with spaces"
        self.root.mkdir()
        (self.root / EXPORT.POLICY).parent.mkdir(parents=True)

    def tearDown(self):
        self.temporary.cleanup()

    def policy(self, include, exclude=()):
        value = {"include": include, "exclude": list(exclude)}
        (self.root / EXPORT.POLICY).write_text(json.dumps(value), encoding="utf-8")
        return value

    def test_only_reviewed_files_and_hashes_are_exported(self):
        (self.root / "backend").mkdir()
        (self.root / "backend/main.go").write_text("package main\n", encoding="utf-8")
        (self.root / "backend/.env").write_text("secret", encoding="utf-8")
        (self.root / "backend/server.exe").write_bytes(b"binary")
        (self.root / "backend/jwt-secret.txt").write_text("private", encoding="utf-8")
        (self.root / "backend/players.sqlite3").write_bytes(b"private database")
        (self.root / "backend/private").mkdir()
        (self.root / "backend/private/account.json").write_text("{}", encoding="utf-8")
        self.policy(["backend/"])
        target = self.base / "public tree"
        result = EXPORT.export(self.root, target)
        self.assertEqual([row["path"] for row in result["files"]], ["backend/main.go"])
        self.assertEqual(EXPORT.sha(self.root / "backend/main.go"), EXPORT.sha(target / "backend/main.go"))
        self.assertFalse(result["published"])
        self.assertFalse((target / ".git").exists())

    def test_existing_destination_and_source_overlap_are_rejected(self):
        self.policy([])
        with self.assertRaises(ValueError):
            EXPORT.export(self.root, self.root)
        with self.assertRaises(ValueError):
            EXPORT.export(self.root, self.root / "public")
        target = self.base / "existing"
        target.mkdir()
        with self.assertRaises(ValueError):
            EXPORT.export(self.root, target)

    def test_missing_required_file_does_not_create_partial_export(self):
        self.policy(["missing.go"])
        target = self.base / "public"
        with self.assertRaises(ValueError):
            EXPORT.export(self.root, target)
        self.assertFalse(target.exists())

    def test_policy_cannot_escape_source(self):
        for name in ("", ".", "./backend", "backend//private", "../private.json", "/absolute", "C:/private", "backend\\private"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                EXPORT.relative_name(name)

    def test_optional_ai_pattern_is_excluded(self):
        value = self.policy(["assets/"], ["assets/ai.png"])
        (self.root / "assets").mkdir()
        (self.root / "assets/ai.png").write_bytes(b"ai")
        (self.root / "assets/legal.png").write_bytes(b"approved")
        self.assertEqual([p.name for p in EXPORT.selected_files(self.root, value)], ["legal.png"])

    def test_exact_public_audit_covers_resources_frontend_and_extra_files(self):
        (self.root / 'frontend').mkdir()
        (self.root / 'frontend/main.ts').write_text('export {}', encoding='utf-8')
        self.policy(['frontend/'])
        target = self.base / 'public'
        EXPORT.export(self.root, target)
        self.assertEqual(AUDIT.audit(target, exact=True)['status'], 'PASS_WITH_REVIEW')
        (target / 'frontend/db-password.txt').write_text('private', encoding='utf-8')
        failures = AUDIT.audit(target, exact=True)['failures']
        self.assertIn('forbidden_file', {row['kind'] for row in failures})
        self.assertIn('invalid_export_manifest', {row['kind'] for row in failures})

    def test_public_placeholders_do_not_hide_unrecognized_secret_values(self):
        candidate = self.root / 'config.txt'
        candidate.write_text('password: "replace-with-a-local-password"', encoding='utf-8')
        self.assertFalse(AUDIT.audit(self.root, exact=True)['failures'])
        candidate.write_text('password: "' + 'a' * 64 + '"', encoding='utf-8')
        self.assertIn('secret_like_text', {row['kind'] for row in AUDIT.audit(self.root, exact=True)['failures']})

    def test_exact_audit_rejects_changed_files_and_missing_link_targets(self):
        (self.root / 'README.md').write_text('[missing](missing.md)', encoding='utf-8')
        self.policy(['README.md'])
        target = self.base / 'public'
        EXPORT.export(self.root, target)
        self.assertIn('broken_or_escaping_link', {row['kind'] for row in AUDIT.audit(target, exact=True)['failures']})
        (target / 'README.md').write_text('changed', encoding='utf-8')
        self.assertIn('invalid_export_manifest', {row['kind'] for row in AUDIT.audit(target, exact=True)['failures']})

    def test_symbolic_link_is_rejected_without_following(self):
        (self.root / "backend").mkdir()
        outside = self.base / "outside.txt"
        outside.write_text("private", encoding="utf-8")
        link = self.root / "backend/link.txt"
        try:
            os.symlink(outside, link)
        except OSError as error:
            self.skipTest("Symlink creation not permitted by this Windows account: " + str(error))
        policy = self.policy(["backend/"])
        with self.assertRaises(ValueError):
            EXPORT.selected_files(self.root, policy)

    def attribute_fixture(self):
        attributes = (b'* -text\n*.md text eol=lf\n*.cs text eol=lf\n*.json text eol=lf\n'
                      b'*.cmd text eol=crlf\n*.png binary\n'
                      b'backend/internal/database/migrations/*.sql text eol=lf\n'
                      b'*.sql -text\ndocs/game-demo/config/** -text\ndocs/game-demo/contracts/** -text\n*.cmd -text\n')
        content = {
            '.gitattributes': attributes,
            'README.md': b'readme\r\nsecond line\r\n',
            'client/control.cs': b'class Control {}\r\n',
            'start.cmd': b'@echo off\necho ready\n',
            'image.png': b'\0bitmap\r\nbytes\n',
            'opaque.bin': b'preserve unspecified\r\n',
            'backend/internal/database/migrations/applied.sql': b'SELECT 1;\r\n',
            'docs/game-demo/config/inherited.json': b'{"source":"config"}\r\n',
            'docs/game-demo/contracts/runtime/inherited.json': b'{"source":"contract"}\r\n',
            'nested/.gitattributes': b'*.md -text\n',
            'nested/README.md': b'child attribute wins\r\n',
        }
        for name, data in content.items():
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        self.policy([*content, EXPORT.POLICY.as_posix()])
        return content

    def test_git_attributes_normalize_only_declared_text_and_keep_protected_source_bytes(self):
        originals = self.attribute_fixture()
        target = self.base / 'public'
        result = EXPORT.export(self.root, target)
        for name, data in originals.items():
            self.assertEqual((self.root / name).read_bytes(), data, name)
        self.assertEqual((target / 'README.md').read_bytes(), b'readme\nsecond line\n')
        self.assertEqual((target / 'client/control.cs').read_bytes(), b'class Control {}\n')
        self.assertEqual((target / 'start.cmd').read_bytes(), originals['start.cmd'])
        for name in originals.keys() - {'README.md', 'client/control.cs'}:
            self.assertEqual((target / name).read_bytes(), originals[name], name)
        self.assertEqual(set(result['line_endings']['normalized_files']), {'README.md', 'client/control.cs'})
        self.assertFalse(result['line_endings']['source_files_modified'])
        self.assertEqual(AUDIT.audit(target, exact=True)['status'], 'PASS_WITH_REVIEW')

    def test_export_manifest_matches_real_git_index_and_windows_linux_checkouts(self):
        self.attribute_fixture()
        target = self.base / 'public'
        result = EXPORT.export(self.root, target)
        environment = {key: value for key, value in os.environ.items() if not key.startswith('GIT_')}
        environment.update(GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL=os.devnull)

        def git(directory, *arguments):
            command = ['git', '-c', 'user.name=Export fixture', '-c', 'user.email=fixture@example.invalid',
                       '-c', 'commit.gpgsign=false', '-c', 'core.attributesFile=' + os.devnull,
                       '-c', 'core.hooksPath=' + str(self.base / 'absent-hooks'),
                       '-C', str(directory), *arguments]
            completed = subprocess.run(command, env=environment, capture_output=True, timeout=20)
            self.assertEqual(completed.returncode, 0, completed.stderr.decode('utf-8', errors='replace'))
            return completed.stdout

        git(target, 'init', '--quiet', '--template=')
        git(target, 'config', 'core.autocrlf', 'true')
        git(target, 'add', '--all')
        git(target, 'commit', '--quiet', '-m', 'Verify exported bytes')
        for row in result['files']:
            blob = git(target, 'show', 'HEAD:' + row['path'])
            expected = (target / row['path']).read_bytes()
            self.assertEqual(blob, expected, row['path'])
        with zipfile.ZipFile(io.BytesIO(git(target, 'archive', '--format=zip', 'HEAD'))) as archive:
            for row in result['files']:
                self.assertEqual(archive.read(row['path']), (target / row['path']).read_bytes(), row['path'])
            self.assertEqual(archive.read('source-export.json'), (target / 'source-export.json').read_bytes())
        for autocrlf in ('false', 'true'):
            clone = self.base / ('clone-' + autocrlf)
            git(self.base, 'clone', '--quiet', '--no-hardlinks', '--config', 'core.autocrlf=' + autocrlf,
                str(target), str(clone))
            for row in result['files']:
                path = clone / row['path']
                self.assertEqual(path.stat().st_size, row['bytes'], row['path'])
                self.assertEqual(EXPORT.sha(path), row['sha256'], row['path'])
            self.assertEqual((clone / 'source-export.json').read_bytes(), (target / 'source-export.json').read_bytes())

    def test_checkout_transformations_without_explicit_export_support_fail_before_output(self):
        for attribute in ('text=auto', 'text', 'filter=custom', 'working-tree-encoding=UTF-16', 'ident'):
            with self.subTest(attribute=attribute):
                (self.root / '.gitattributes').write_text('*.txt ' + attribute + '\n', encoding='utf-8')
                (self.root / 'source.txt').write_bytes(b'source\r\n')
                self.policy(['.gitattributes', 'source.txt'])
                target = self.base / ('unsupported-' + str(len(attribute)))
                with self.assertRaises(ValueError):
                    EXPORT.export(self.root, target)
                self.assertFalse(target.exists())


if __name__ == "__main__":
    unittest.main()
