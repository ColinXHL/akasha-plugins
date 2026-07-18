import importlib.util
import json
import unittest
from pathlib import Path


SCRIPT = (
    Path(__file__).parents[2]
    / ".github"
    / "scripts"
    / "sync_bettergi_blacklist.py"
)
SPEC = importlib.util.spec_from_file_location("sync_bettergi_blacklist", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class SyncBetterGiBlacklistTests(unittest.TestCase):
    def test_normalize_removes_blanks_but_preserves_duplicates(self):
        encoded, count, unique_count = MODULE.normalize_blacklist(
            json.dumps(["甲", "", "  ", "甲", "乙"], ensure_ascii=False).encode()
        )

        self.assertEqual(["甲", "甲", "乙"], json.loads(encoded))
        self.assertEqual(3, count)
        self.assertEqual(2, unique_count)

    def test_normalize_rejects_non_string_entries(self):
        with self.assertRaises(ValueError):
            MODULE.normalize_blacklist(b'["ok", 1]')

    def test_update_notice_changed_content_populates_resource(self):
        notice = self._notice()
        content = '["甲","乙"]'.encode()

        updated, resource_changed, resource = MODULE.update_notice(
            notice,
            "0.62.0",
            content,
            "plugins/automation/pick-blacklist/0.62.0.json",
            "https://update.example.test/plugins/automation/pick-blacklist/0.62.0.json",
        )

        self.assertTrue(resource_changed)
        self.assertEqual(2, updated["schemaVersion"])
        self.assertEqual("bettergi-0.62.0", resource["version"])
        self.assertEqual("0.62.0", resource["upstreamRelease"])
        self.assertEqual("0.3.3", resource["minPluginVersion"])
        self.assertEqual(2, resource["entryCount"])
        self.assertEqual(len(content), resource["size"])

    def test_update_notice_same_content_only_advances_upstream_release(self):
        content = b'["same"]'
        notice = self._notice()
        _, _, previous = MODULE.update_notice(
            notice,
            "0.61.0",
            content,
            "prefix/0.61.0.json",
            "https://update.example.test/prefix/0.61.0.json",
        )
        previous_url = previous["url"]

        _, resource_changed, current = MODULE.update_notice(
            notice,
            "0.62.0",
            content,
            "prefix/0.62.0.json",
            "https://update.example.test/prefix/0.62.0.json",
        )

        self.assertFalse(resource_changed)
        self.assertEqual("0.62.0", current["upstreamRelease"])
        self.assertEqual(previous_url, current["url"])
        self.assertEqual("bettergi-0.61.0", current["version"])

    def test_public_resource_url_uses_notice_origin(self):
        self.assertEqual(
            "https://update.example.test/prefix/0.62.0.json",
            MODULE.public_resource_url(
                "https://update.example.test/notice.json",
                "prefix/0.62.0.json",
            ),
        )
        self.assertEqual(
            "https://update.example.test/prefix/0.62.0.json",
            MODULE.public_resource_url(
                "http://update.example.test/notice.json",
                "prefix/0.62.0.json",
            ),
        )

    def test_current_upstream_release_handles_null_resources(self):
        notice = self._notice()
        notice["plugins"][MODULE.PLUGIN_ID]["resources"] = None

        self.assertIsNone(MODULE.current_upstream_release(notice))

    @staticmethod
    def _notice():
        return {
            "schemaVersion": 2,
            "stable": {"version": "1.2.1"},
            "plugins": {
                MODULE.PLUGIN_ID: {
                    "version": "0.3.2",
                    "resources": {},
                }
            },
        }


if __name__ == "__main__":
    unittest.main()
