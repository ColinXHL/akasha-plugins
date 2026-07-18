#!/usr/bin/env python3
"""Unit tests for repository validation and deterministic index generation."""

from __future__ import annotations

import importlib.util
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from types import ModuleType


ROOT = Path(__file__).resolve().parents[1]


def load_repository_tool() -> ModuleType:
    module_path = ROOT / "build" / "repository_tool.py"
    spec = importlib.util.spec_from_file_location("repository_tool", module_path)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load repository_tool.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


repository_tool = load_repository_tool()


class RepositoryToolTests(unittest.TestCase):
    def test_repository_generates_stable_sorted_index(self) -> None:
        records = repository_tool.discover_plugins(
            ROOT,
            require_release_integrity=False,
        )
        first = repository_tool.build_index(records, "0" * 40)
        second = repository_tool.build_index(records, "0" * 40)

        self.assertEqual(first, second)
        self.assertEqual(
            [
                "bilibili-page-list",
                "genshin-direction-marker",
                "smart-cursor-detection",
            ],
            [plugin["id"] for plugin in first["plugins"]],
        )

    def test_stage_catalog_rejects_output_outside_repository(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            outside = Path(temporary)
            with self.assertRaises(repository_tool.RepositoryValidationError):
                repository_tool.reset_staging_directory(ROOT, outside)

    def test_write_json_is_deterministic_and_ends_with_newline(self) -> None:
        value = {
            "schemaVersion": 1,
            "commit": "0" * 40,
            "plugins": [],
        }
        with tempfile.TemporaryDirectory(dir=ROOT) as temporary:
            output = Path(temporary) / "repo.json"
            repository_tool.write_json(output, value)
            first = output.read_bytes()
            repository_tool.write_json(output, value)
            second = output.read_bytes()

        self.assertEqual(first, second)
        self.assertTrue(first.endswith(b"\n"))
        self.assertEqual(value, json.loads(first))

    def test_plugin_discovery_is_sorted_and_requires_entry_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            fixture = self.create_repository_fixture(Path(temporary))
            self.create_repository_plugin(fixture, "z-plugin", "1.0.0")
            self.create_repository_plugin(fixture, "a-plugin", "2.0.0")

            records = repository_tool.discover_plugins(
                fixture,
                require_release_integrity=False,
            )
            self.assertEqual(
                ["a-plugin", "z-plugin"],
                [record.manifest["id"] for record in records],
            )

            (fixture / "plugins" / "a-plugin" / "main.js").unlink()
            with self.assertRaises(repository_tool.RepositoryValidationError):
                repository_tool.discover_plugins(
                    fixture,
                    require_release_integrity=False,
                )

    def test_catalog_staging_filters_development_and_binary_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            fixture = self.create_repository_fixture(Path(temporary))
            plugin = self.create_repository_plugin(
                fixture,
                "repository-plugin",
                "1.0.0",
            )
            (plugin / "assets").mkdir()
            (plugin / "assets" / "icon.png").write_bytes(b"png")
            (plugin / "tests").mkdir()
            (plugin / "tests" / "plugin.test.js").write_text(
                "throw new Error();\n",
                encoding="utf-8",
            )
            (plugin / "Worker.exe").write_bytes(b"not-an-executable")

            output = fixture / "artifacts" / "catalog"
            records = repository_tool.discover_plugins(
                fixture,
                require_release_integrity=True,
            )
            repository_tool.reset_staging_directory(fixture, output)
            repository_tool.copy_plugin_to_catalog(
                records[0],
                output / "plugins" / "repository-plugin",
            )

            published = output / "plugins" / "repository-plugin"
            self.assertTrue((published / "main.js").is_file())
            self.assertTrue((published / "assets" / "icon.png").is_file())
            self.assertFalse((published / "tests").exists())
            self.assertFalse((published / "Worker.exe").exists())

    @staticmethod
    def create_repository_fixture(root: Path) -> Path:
        shutil.copytree(ROOT / "schemas", root / "schemas")
        shutil.copy2(ROOT / "LICENSE", root / "LICENSE")
        (root / "plugins").mkdir()
        return root

    @staticmethod
    def create_repository_plugin(
        root: Path,
        plugin_id: str,
        version: str,
    ) -> Path:
        plugin = root / "plugins" / plugin_id
        plugin.mkdir()
        manifest = {
            "manifestVersion": 2,
            "id": plugin_id,
            "name": plugin_id,
            "version": version,
            "description": f"Description for {plugin_id}.",
            "authors": [{"name": "Contract Test"}],
            "host": {"minVersion": "1.4.0"},
            "main": "main.js",
            "permissions": [],
            "savedFiles": [],
            "distribution": {"type": "repository"},
        }
        repository_tool.write_json(plugin / "manifest.json", manifest)
        (plugin / "README.md").write_text(
            f"# {plugin_id}\n",
            encoding="utf-8",
        )
        (plugin / "main.js").write_text(
            "export default {};\n",
            encoding="utf-8",
        )
        return plugin


if __name__ == "__main__":
    unittest.main()
