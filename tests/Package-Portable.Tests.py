import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location('package_portable', Path(__file__).resolve().parents[1] / 'scripts/package_portable.py')
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)

class PortablePackageTests(unittest.TestCase):
    def setUp(self):
        self.workspace = tempfile.TemporaryDirectory(prefix='nexus-package-tests-')
        self.root = Path(self.workspace.name)
        self.published = self.root / 'published'
        for name in package.REQUIRED:
            path = self.published / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(name.encode())
        config = {'Storage': {'RootPath': ''}, 'Database': {'Path': ''}, 'Logging': {'Directory': ''}}
        (self.published / 'appsettings.json').write_text(json.dumps(config), encoding='utf-8')
        self.update_manifest()
        self.archive = self.root / 'NexusExplorer_new.zip'

    def tearDown(self):
        self.workspace.cleanup()

    def update_manifest(self):
        digest = hashlib.sha256((self.published / package.NATIVE).read_bytes()).hexdigest()
        (self.published / package.MANIFEST).write_text(json.dumps({'dllSha256': digest}), encoding='utf-8')

    def test_flat_zip_excludes_data_docs_tests_and_replaces_only_the_same_archive(self):
        for name in ('Storage/private.mp4', 'data/nexus.db', 'logs/private.log', 'README.md', 'NexusExplorer.Tests.dll', 'NexusExplorer.pdb'):
            path = self.published / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b'EXCLUDED')
        names = package.create_archive(self.published, self.archive)
        self.assertEqual(package.REQUIRED, set(names))
        package.extract_verified(self.archive, self.root / 'first-extraction')
        (self.published / 'NexusExplorer.exe').write_bytes(b'NEW EXE')
        package.create_archive(self.published, self.archive)
        with zipfile.ZipFile(self.archive) as z:
            self.assertEqual(b'NEW EXE', z.read('NexusExplorer.exe'))
        self.assertEqual([self.archive], list(self.root.glob('*.zip')))

    def test_bad_native_hash_does_not_replace_existing_package(self):
        package.create_archive(self.published, self.archive)
        original = self.archive.read_bytes()
        (self.published / package.NATIVE).write_bytes(b'WRONG DLL')
        with self.assertRaises(ValueError):
            package.create_archive(self.published, self.archive)
        self.assertEqual(original, self.archive.read_bytes())
        self.assertFalse(self.archive.with_name('.' + self.archive.name + '.tmp').exists())

    def test_user_config_paths_and_missing_runtime_are_rejected(self):
        config = {'Storage': {'RootPath': 'D:/USER-DATA'}, 'Database': {'Path': ''}, 'Logging': {'Directory': ''}}
        (self.published / 'appsettings.json').write_text(json.dumps(config), encoding='utf-8')
        with self.assertRaises(ValueError):
            package.create_archive(self.published, self.archive)
        self.assertFalse(self.archive.exists())
        (self.published / 'e_sqlite3.dll').unlink()
        with self.assertRaises(ValueError):
            package.create_archive(self.published, self.archive)

    def test_unexpected_archive_paths_are_rejected_before_extraction(self):
        package.create_archive(self.published, self.archive)
        with zipfile.ZipFile(self.archive, 'a') as z:
            z.writestr('../outside.txt', b'NO')
        destination = self.root / 'extracted'
        with self.assertRaises(ValueError):
            package.extract_verified(self.archive, destination)
        self.assertFalse(destination.exists())
        self.assertFalse((self.root.parent / 'outside.txt').exists())

if __name__ == '__main__':
    unittest.main()
