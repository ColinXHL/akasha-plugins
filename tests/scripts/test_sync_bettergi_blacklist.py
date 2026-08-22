#!/usr/bin/env python3
"""Unit tests for the BetterGI blacklist synchronization planner."""

from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from types import ModuleType


ROOT = Path(__file__).resolve().parents[2]


def load_script() -> ModuleType:
    path = ROOT / ".github" / "scripts" / "sync_bettergi_blacklist.py"
    spec = importlib.util.spec_from_file_location("sync_bettergi_blacklist", path)
    if spec is None or spec.loader is None:
        raise RuntimeError("could not load sync script")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


sync = load_script()


class SyncBetterGiBlacklistTests(unittest.TestCase):
    def test_build_plan_generates_content_addressed_release(self) -> None:
        payload = json.dumps(
            [f"entry-{index}" for index in range(1000)],
            ensure_ascii=False,
        ).encode("utf-8")
        current_digest = "a" * 64
        catalog = self.create_catalog(current_digest, "0.63.0")
        release = self.create_release("0.64.0")

        with tempfile.TemporaryDirectory() as temporary:
            plan = sync.build_plan(
                catalog,
                release,
                payload,
                Path(temporary),
            )
            resource = plan["updatedCatalog"]["resources"][0]
            asset_path = Path(plan["assetPath"])

            self.assertTrue(plan["resourceChanged"])
            self.assertTrue(plan["metadataChanged"])
            self.assertEqual("0.64.0", resource["sourceVersion"])
            self.assertEqual(resource["revision"], resource["distribution"]["sha256"])
            self.assertTrue(asset_path.is_file())
            self.assertEqual(payload, asset_path.read_bytes())
            self.assertIn(resource["revision"][:12], plan["resourceTag"])

    def test_build_plan_updates_source_version_without_new_revision(self) -> None:
        payload = json.dumps([f"entry-{index}" for index in range(1000)]).encode()
        import hashlib

        digest = hashlib.sha256(payload).hexdigest()
        catalog = self.create_catalog(digest, "0.63.0")
        with tempfile.TemporaryDirectory() as temporary:
            plan = sync.build_plan(
                catalog,
                self.create_release("v0.64.0"),
                payload,
                Path(temporary),
            )

        self.assertFalse(plan["resourceChanged"])
        self.assertTrue(plan["metadataChanged"])
        self.assertEqual(
            "0.64.0",
            plan["updatedCatalog"]["resources"][0]["sourceVersion"],
        )

    def test_rejects_small_or_malformed_blacklist(self) -> None:
        with self.assertRaises(ValueError):
            sync.validate_blacklist(b"{}")
        with self.assertRaises(ValueError):
            sync.validate_blacklist(b'["only one"]')

    def test_numeric_version_normalizes_v_prefix_and_missing_components(self) -> None:
        self.assertEqual(sync.numeric_version("0.63"), sync.numeric_version("v0.63.0"))
        self.assertLess(sync.numeric_version("0.63.0"), sync.numeric_version("0.64.0"))

    @staticmethod
    def create_release(tag: str) -> dict:
        return {
            "tag_name": tag,
            "html_url": f"https://github.com/upstream/releases/tag/{tag}",
            "assets": [
                {
                    "name": f"BetterGI_v{tag.removeprefix('v')}.7z",
                    "browser_download_url": "https://example.invalid/BetterGI.7z",
                }
            ],
        }

    @staticmethod
    def create_catalog(digest: str, source_version: str) -> dict:
        return {
            "schemaVersion": 1,
            "pluginId": sync.PLUGIN_ID,
            "resources": [
                {
                    "id": sync.RESOURCE_ID,
                    "revision": digest,
                    "sourceVersion": source_version,
                    "optional": True,
                    "distribution": {
                        "type": "release",
                        "tag": f"old-{digest[:12]}",
                        "asset": f"old.{digest[:12]}.json",
                        "size": 1,
                        "sha256": digest,
                    },
                }
            ],
        }


if __name__ == "__main__":
    unittest.main()
