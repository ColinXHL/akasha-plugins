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


if __name__ == "__main__":
    unittest.main()
