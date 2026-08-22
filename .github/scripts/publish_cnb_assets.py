#!/usr/bin/env python3
"""Create or reuse a CNB Release and upload generic verified assets."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path
from urllib.parse import quote

import requests


CNB_API_BASE = "https://api.cnb.cool"
PROJECT_PATTERN = re.compile(r"^[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)+$")
TAG_PATTERN = re.compile(r"^[A-Za-z0-9._-]+$")


class Publisher:
    def __init__(self, token: str, project: str) -> None:
        self.project = project
        self.session = requests.Session()
        self.session.headers.update(
            {
                "Accept": "application/json",
                "Authorization": f"Bearer {token}",
                "User-Agent": "Akasha-Plugins-Resource-Release/1.0",
            }
        )

    def get_release(self, tag: str) -> dict | None:
        encoded = quote(tag, safe="")
        url = f"{CNB_API_BASE}/{self.project}/-/releases/tags/{encoded}"
        response = self.session.get(url, timeout=(15, 60))
        if response.status_code == 404:
            return None
        response.raise_for_status()
        release = response.json()
        if not release.get("id"):
            raise RuntimeError("CNB Release lookup response has no id")
        return release

    def create_release(self, tag: str, name: str, body: str) -> dict:
        url = f"{CNB_API_BASE}/{self.project}/-/releases"
        response = self.session.post(
            url,
            json={
                "tag_name": tag,
                "name": name,
                "body": body,
                "draft": False,
                "prerelease": False,
                "target_commitish": "main",
                "make_latest": "false",
            },
            timeout=(15, 60),
        )
        response.raise_for_status()
        release = response.json()
        if not release.get("id"):
            raise RuntimeError("CNB Release creation response has no id")
        return release

    def upload(self, release_id: str, asset: Path) -> None:
        url = (
            f"{CNB_API_BASE}/{self.project}/-/releases/"
            f"{release_id}/asset-upload-url"
        )
        response = self.session.post(
            url,
            json={
                "asset_name": asset.name,
                "overwrite": True,
                "size": asset.stat().st_size,
            },
            timeout=(15, 60),
        )
        response.raise_for_status()
        info = response.json()
        upload_url = info.get("upload_url")
        if not upload_url:
            raise RuntimeError(f"CNB upload URL missing for {asset.name}")
        with asset.open("rb") as stream:
            upload = self.session.put(upload_url, data=stream, timeout=(30, 900))
        upload.raise_for_status()
        if info.get("verify_url"):
            verify = self.session.post(info["verify_url"], timeout=(15, 120))
            verify.raise_for_status()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--token", required=True)
    parser.add_argument("--project", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--body", required=True)
    parser.add_argument("--assets", required=True, type=Path, nargs="+")
    args = parser.parse_args()
    if not args.token:
        raise ValueError("CNB_TOKEN is empty")
    if not PROJECT_PATTERN.fullmatch(args.project):
        raise ValueError("invalid CNB project path")
    if not TAG_PATTERN.fullmatch(args.tag):
        raise ValueError("invalid Release tag")
    assets = [path.resolve() for path in args.assets]
    if any(not path.is_file() or path.stat().st_size <= 0 for path in assets):
        raise ValueError("all assets must be non-empty files")

    publisher = Publisher(args.token, args.project)
    release = publisher.get_release(args.tag)
    if release is None:
        release = publisher.create_release(args.tag, args.name, args.body)
        print(f"Created CNB Release {args.tag}")
    else:
        print(f"Reusing CNB Release {args.tag}")
    for asset in assets:
        publisher.upload(str(release["id"]), asset)
        print(f"Uploaded {asset.name}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"CNB asset publication failed: {error}", file=sys.stderr)
        raise SystemExit(1)
