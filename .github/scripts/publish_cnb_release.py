#!/usr/bin/env python3
"""Publish an already verified Akasha plugin package to a CNB Release."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

import requests


CNB_API_BASE = "https://api.cnb.cool"
PLUGIN_ID = "akasha-genshin-automation"
PROJECT_PATH_PATTERN = re.compile(r"^[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)+$")
VERSION_PATTERN = re.compile(
    r"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$"
)


def parse_bool(value: str) -> bool:
    normalized = value.strip().lower()
    if normalized in {"true", "1", "yes"}:
        return True
    if normalized in {"false", "0", "no"}:
        return False
    raise argparse.ArgumentTypeError(f"invalid boolean value: {value}")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_assets(asset_directory: Path, version: str) -> list[Path]:
    archive = asset_directory / f"{PLUGIN_ID}-{version}-win-x64.zip"
    checksum = Path(f"{archive}.sha256")
    for path in (archive, checksum):
        if not path.is_file():
            raise ValueError(f"release asset does not exist: {path}")

    checksum_fields = checksum.read_text(encoding="ascii").strip().split()
    if not checksum_fields:
        raise ValueError("SHA-256 file is empty")

    expected_hash = checksum_fields[0].lower()
    actual_hash = sha256(archive)
    if expected_hash != actual_hash:
        raise ValueError(
            f"archive SHA-256 mismatch: expected {expected_hash}, got {actual_hash}"
        )

    return [archive, checksum]


class CnbReleasePublisher:
    def __init__(self, token: str, project_path: str) -> None:
        self._project_path = project_path
        self._session = requests.Session()
        self._session.headers.update(
            {
                "Accept": "application/json",
                "Authorization": f"Bearer {token}",
                "User-Agent": "Akasha-Plugins-Release/1.0",
            }
        )

    def create_release(
        self,
        version: str,
        draft: bool,
        prerelease: bool,
    ) -> dict:
        url = f"{CNB_API_BASE}/{self._project_path}/-/releases"
        payload = {
            "tag_name": f"{PLUGIN_ID}-v{version}",
            "name": f"Akasha Genshin Automation v{version}",
            "body": (
                f"Akasha Genshin Automation v{version} automated release. "
                "The ZIP is identical to the AkashaPlugins GitHub Release asset."
            ),
            "draft": draft,
            "prerelease": prerelease,
            "target_commitish": "main",
            "make_latest": "false" if prerelease else "true",
        }
        response = self._session.post(url, json=payload, timeout=(15, 60))
        if not response.ok:
            raise RuntimeError(
                f"CNB Release creation failed: HTTP {response.status_code}: "
                f"{response.text[:1000]}"
            )

        release = response.json()
        if not release.get("id"):
            raise RuntimeError("CNB Release response did not contain an id")
        return release

    def upload_asset(self, release_id: str, asset_path: Path) -> None:
        prepare_url = (
            f"{CNB_API_BASE}/{self._project_path}/-/releases/"
            f"{release_id}/asset-upload-url"
        )
        prepare_response = self._session.post(
            prepare_url,
            json={
                "asset_name": asset_path.name,
                "overwrite": True,
                "size": asset_path.stat().st_size,
            },
            timeout=(15, 60),
        )
        if not prepare_response.ok:
            raise RuntimeError(
                f"CNB asset URL request failed for {asset_path.name}: "
                f"HTTP {prepare_response.status_code}: {prepare_response.text[:1000]}"
            )

        upload_info = prepare_response.json()
        upload_url = upload_info.get("upload_url")
        if not upload_url:
            raise RuntimeError(
                f"CNB asset URL response did not contain upload_url: {asset_path.name}"
            )

        with asset_path.open("rb") as stream:
            upload_response = self._session.put(
                upload_url,
                data=stream,
                timeout=(30, 900),
            )
        if not upload_response.ok:
            raise RuntimeError(
                f"CNB asset upload failed for {asset_path.name}: "
                f"HTTP {upload_response.status_code}: {upload_response.text[:1000]}"
            )

        verify_url = upload_info.get("verify_url")
        if verify_url:
            verify_response = self._session.post(
                verify_url,
                timeout=(15, 120),
            )
            if not verify_response.ok:
                raise RuntimeError(
                    f"CNB asset verification failed for {asset_path.name}: "
                    f"HTTP {verify_response.status_code}: {verify_response.text[:1000]}"
                )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--token", default="")
    parser.add_argument("--project", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--assets", required=True, type=Path)
    parser.add_argument("--draft", required=True, type=parse_bool)
    parser.add_argument("--prerelease", required=True, type=parse_bool)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    if not PROJECT_PATH_PATTERN.fullmatch(args.project):
        raise ValueError(f"invalid CNB project path: {args.project}")
    if not VERSION_PATTERN.fullmatch(args.version):
        raise ValueError(f"invalid release version: {args.version}")

    assets = validate_assets(args.assets.resolve(), args.version)
    if args.dry_run:
        print(
            json.dumps(
                {
                    "project": args.project,
                    "version": args.version,
                    "draft": args.draft,
                    "prerelease": args.prerelease,
                    "assets": [str(path) for path in assets],
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 0

    if not args.token:
        raise ValueError("CNB_TOKEN is empty")

    publisher = CnbReleasePublisher(args.token, args.project)
    release = publisher.create_release(args.version, args.draft, args.prerelease)
    release_id = str(release["id"])
    print(f"Created CNB Release {release.get('name', release_id)}")

    for asset in assets:
        print(f"Uploading {asset.name} ({asset.stat().st_size} bytes)")
        publisher.upload_asset(release_id, asset)
        print(f"Uploaded and verified {asset.name}")

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"CNB publish failed: {error}", file=sys.stderr)
        raise SystemExit(1)
