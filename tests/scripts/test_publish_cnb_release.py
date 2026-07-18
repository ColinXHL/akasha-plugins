import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path


SCRIPT = (
    Path(__file__).parents[2]
    / ".github"
    / "scripts"
    / "publish_cnb_release.py"
)
SPEC = importlib.util.spec_from_file_location(
    "publish_cnb_release",
    SCRIPT,
)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class PublishCnbReleaseTests(unittest.TestCase):
    def test_validate_assets_accepts_namespaced_release_package(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = (
                root
                / "akasha-genshin-automation-0.4.3-win-x64.zip"
            )
            archive.write_bytes(b"verified-package")
            digest = hashlib.sha256(archive.read_bytes()).hexdigest()
            Path(f"{archive}.sha256").write_text(
                f"{digest} *{archive.name}",
                encoding="ascii",
            )

            assets = MODULE.validate_assets(root, "0.4.3")

            self.assertEqual(
                [archive, Path(f"{archive}.sha256")],
                assets,
            )

    def test_validate_assets_rejects_legacy_archive_name(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = (
                root
                / "akasha-genshin-automation-v0.4.3.zip"
            )
            archive.write_bytes(b"legacy")
            Path(f"{archive}.sha256").write_text(
                hashlib.sha256(archive.read_bytes()).hexdigest(),
                encoding="ascii",
            )

            with self.assertRaises(ValueError):
                MODULE.validate_assets(root, "0.4.3")


if __name__ == "__main__":
    unittest.main()
