#!/usr/bin/env python3
"""Regression checks for the Phase 4 built-in plugin migration."""

from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]

EXPECTED_PLUGINS = {
    "bilibili-page-list": {
        "version": "1.2.1",
        "permissions": {
            "events",
            "hotkey",
            "network",
            "panel",
            "player",
            "subtitle",
        },
        "files": {"main.js", "settings_ui.json"},
    },
    "genshin-direction-marker": {
        "version": "1.1.0",
        "permissions": {"overlay", "subtitle"},
        "files": {"main.js", "settings_ui.json", "assets/right.png"},
    },
    "smart-cursor-detection": {
        "version": "1.0.0",
        "permissions": {"events", "window"},
        "files": {"main.js", "settings_ui.json"},
    },
}


class MigratedPluginTests(unittest.TestCase):
    def test_migrated_plugins_preserve_frozen_ids_versions_and_permissions(
        self,
    ) -> None:
        plugin_root = ROOT / "plugins"
        self.assertLessEqual(
            set(EXPECTED_PLUGINS),
            {path.name for path in plugin_root.iterdir() if path.is_dir()},
        )

        for plugin_id, expected in EXPECTED_PLUGINS.items():
            with self.subTest(plugin_id=plugin_id):
                plugin = plugin_root / plugin_id
                manifest = json.loads(
                    (plugin / "manifest.json").read_text(encoding="utf-8")
                )
                self.assertEqual(plugin_id, manifest["id"])
                self.assertEqual(expected["version"], manifest["version"])
                self.assertEqual(
                    expected["permissions"],
                    set(manifest["permissions"]),
                )
                self.assertEqual(
                    {"type": "repository"},
                    manifest["distribution"],
                )
                self.assertFalse((plugin / "plugin.json").exists())
                for relative_path in expected["files"]:
                    self.assertTrue((plugin / relative_path).is_file())

    def test_automation_migration_preserves_version_and_unified_layout(
        self,
    ) -> None:
        plugin = ROOT / "plugins" / "akasha-genshin-automation"
        manifest = json.loads(
            (plugin / "manifest.json").read_text(encoding="utf-8")
        )

        self.assertEqual(
            "akasha-genshin-automation",
            manifest["id"],
        )
        self.assertEqual("0.4.3", manifest["version"])
        self.assertEqual(
            {"companion", "hotkey"},
            set(manifest["permissions"]),
        )
        self.assertEqual("release", manifest["distribution"]["type"])
        self.assertNotIn("sha256", manifest["distribution"])
        self.assertNotIn("size", manifest["distribution"])
        self.assertEqual(
            "runtime/AkashaAutomation.Worker.exe",
            manifest["backend"]["entry"],
        )
        for relative_path in (
            "frontend/main.js",
            "frontend/settings_ui.json",
            "backend/AkashaAutomation.sln",
            "backend/src/AkashaAutomation.Worker/"
            "AkashaAutomation.Worker.csproj",
            "backend/tests/AkashaAutomation.Worker.IntegrationTests/"
            "AkashaAutomation.Worker.IntegrationTests.csproj",
            "packaging/Publish-Plugin.ps1",
            "DERIVATION.md",
            "THIRD_PARTY_NOTICES.md",
        ):
            self.assertTrue((plugin / relative_path).exists())


if __name__ == "__main__":
    unittest.main()
