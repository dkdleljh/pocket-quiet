"""Packaging integrity checks; run: python -m unittest discover -s tests -p 'test_*.py'."""
import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import subprocess

spec = importlib.util.spec_from_file_location("package", Path(__file__).parents[1] / "scripts/package.py")
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


class PackageIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / "app.dll").write_bytes(b"payload")
        self.manifest = {"Version": package.VERSION, "Files": {"app.dll": package.sha256(self.root / "app.dll")}}
        self.save()

    def tearDown(self):
        self.temp.cleanup()

    def save(self):
        (self.root / "deployment-manifest.json").write_text(json.dumps(self.manifest))

    def test_intact(self):
        package.verify(self.root)

    def test_tampered_payload(self):
        (self.root / "app.dll").write_bytes(b"changed")
        with self.assertRaises(SystemExit):
            package.verify(self.root)

    def test_unlisted_executable(self):
        (self.root / "unexpected.exe").write_bytes(b"extra")
        with self.assertRaises(SystemExit):
            package.verify(self.root)

    def test_missing_file(self):
        (self.root / "app.dll").unlink()
        with self.assertRaises(SystemExit):
            package.verify(self.root)

    def test_wrong_version(self):
        self.manifest["Version"] = "0.0.0"
        self.save()
        with self.assertRaises(SystemExit):
            package.verify(self.root)

    def test_path_escape(self):
        self.manifest["Files"] = {"../outside.dll": "0" * 64}
        self.save()
        with self.assertRaises(SystemExit):
            package.verify(self.root)

    def test_finalize_rejects_changed_non_signable_file(self):
        doc = self.root / "README.md"
        doc.write_text("reviewed")
        self.manifest["Files"]["README.md"] = package.sha256(doc)
        self.save()
        doc.write_text("changed after review")
        with patch.object(package, "os", SimpleNamespace(name="nt")), patch.object(package.subprocess, "run") as check:
            with self.assertRaises(SystemExit):
                package.finalize_signed(self.root)
            check.assert_not_called()

    def test_finalize_failure_preserves_manifest(self):
        original = (self.root / "deployment-manifest.json").read_bytes()
        (self.root / "app.dll").write_bytes(b"unsigned replacement")
        with patch.object(package, "os", SimpleNamespace(name="nt")), patch.object(
            package.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "signature verification")
        ):
            with self.assertRaises(subprocess.CalledProcessError):
                package.finalize_signed(self.root)
        self.assertEqual(original, (self.root / "deployment-manifest.json").read_bytes())

    def test_finalize_rehashes_after_current_signature_check(self):
        (self.root / "app.dll").write_bytes(b"signed replacement")
        def verify_current_files(command, check):
            self.assertTrue(check)
            self.assertIn("-RequireValid", command)
            (self.root / "signatures.json").write_text('{"AllValid": true}')
        with patch.object(package, "os", SimpleNamespace(name="nt")), patch.object(
            package.subprocess, "run", side_effect=verify_current_files
        ) as check, patch.object(package, "archive_package") as archive:
            package.finalize_signed(self.root)
            check.assert_called_once()
            archive.assert_called_once_with(self.root.resolve())
        package.verify(self.root)
        updated = json.loads((self.root / "deployment-manifest.json").read_text())
        self.assertEqual(package.sha256(self.root / "app.dll"), updated["Files"]["app.dll"])
        self.assertIn("signatures.json", updated["Files"])


if __name__ == "__main__":
    unittest.main()
