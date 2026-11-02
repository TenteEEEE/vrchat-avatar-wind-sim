"""Regression checks for the metadata gate used before publishing releases."""
import importlib.util
import json
import unittest
from pathlib import Path
from unittest.mock import patch


spec = importlib.util.spec_from_file_location("verify_package", Path(__file__).with_name("verify-package.py"))
verify = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verify)


class ReleaseMetadataTests(unittest.TestCase):
    def setUp(self):
        verify.ERRORS.clear()

    def notes(self, changelog, tag="v1.0.0"):
        def read(path):
            if path.name == "package.json":
                return json.dumps({"version": "1.0.0"})
            return changelog

        with patch.object(verify, "text", side_effect=read):
            return verify.release_notes(tag)

    def test_current_entry_only(self):
        notes = self.notes("# Changelog\n\n## 1.0.0 - 2026-11-02\n\n正式版。\n\n---\n\nDevelopment history.\n\n## 0.3.3\n\nOld changes.\n")
        self.assertEqual(notes, "正式版。\n")
        self.assertEqual(verify.ERRORS, [])

    def test_unprefixed_tag(self):
        self.assertEqual(self.notes("## 1.0.0\n\nRelease.\n", "1.0.0"), "Release.\n")
        self.assertEqual(verify.ERRORS, [])

    def test_tag_mismatch(self):
        self.notes("## 1.0.0\n\nRelease.\n", "v1.0.1")
        self.assertTrue(any("does not match package version" in error for error in verify.ERRORS))

    def test_outdated_changelog(self):
        self.assertIsNone(self.notes("## 0.3.3\n\nOld release.\n"))
        self.assertTrue(verify.ERRORS)

    def test_empty_entry(self):
        self.assertIsNone(self.notes("## 1.0.0\n\n## 0.3.3\n\nOld release.\n"))
        self.assertTrue(verify.ERRORS)

    def test_missing_entry(self):
        self.assertIsNone(self.notes("# Changelog\n"))
        self.assertTrue(verify.ERRORS)


if __name__ == "__main__":
    unittest.main()
