#!/usr/bin/env python3
"""Prepare a verified BetterGI pickup-blacklist resource and updated notice.json."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any


GITHUB_RELEASE_URL = (
    "https://api.github.com/repos/babalae/better-genshin-impact/releases/latest"
)
ARCHIVE_ENTRY = "BetterGI/Assets/Config/Pick/default_pick_black_lists.json"
PLUGIN_ID = "akasha-genshin-automation"
RESOURCE_KEY = "pickBlacklist"


def request_bytes(url: str, token: str | None = None) -> tuple[bytes, dict[str, str]]:
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": "akasha-automation-blacklist-sync",
    }
    if token:
        headers["Authorization"] = f"Bearer {token}"
    with urllib.request.urlopen(
        urllib.request.Request(url, headers=headers), timeout=60
    ) as response:
        return response.read(), {key.lower(): value for key, value in response.headers.items()}


def download_file(url: str, destination: Path, token: str | None = None) -> tuple[int, str]:
    headers = {"User-Agent": "akasha-automation-blacklist-sync"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    size = 0
    digest = hashlib.sha256()
    with urllib.request.urlopen(
        urllib.request.Request(url, headers=headers), timeout=120
    ) as response, destination.open("xb") as output:
        while chunk := response.read(1024 * 1024):
            output.write(chunk)
            size += len(chunk)
            digest.update(chunk)
    return size, digest.hexdigest()


def load_latest_release(token: str | None) -> dict[str, Any]:
    payload, _ = request_bytes(GITHUB_RELEASE_URL, token)
    release = json.loads(payload)
    if release.get("draft") or release.get("prerelease"):
        raise RuntimeError("GitHub latest release unexpectedly returned a draft or prerelease")
    return release


def select_archive_asset(release: dict[str, Any]) -> dict[str, Any]:
    version = str(release["tag_name"]).removeprefix("v")
    expected_name = f"BetterGI_v{version}.7z"
    matches = [
        asset for asset in release.get("assets", []) if asset.get("name") == expected_name
    ]
    if len(matches) != 1:
        raise RuntimeError(f"Expected exactly one release asset named {expected_name}")
    asset = matches[0]
    digest = str(asset.get("digest") or "")
    if not digest.startswith("sha256:") or len(digest) != len("sha256:") + 64:
        raise RuntimeError(f"Release asset {expected_name} has no usable SHA-256 digest")
    if int(asset.get("size") or 0) <= 0:
        raise RuntimeError(f"Release asset {expected_name} has no valid size")
    return asset


def extract_blacklist(archive: Path, output_directory: Path, seven_zip: str) -> Path:
    extract_directory = output_directory / "extracted"
    extract_directory.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [
            seven_zip,
            "x",
            "-y",
            f"-o{extract_directory}",
            "--",
            str(archive),
            ARCHIVE_ENTRY.replace("/", "\\"),
        ],
        check=True,
    )
    extracted = extract_directory / Path(ARCHIVE_ENTRY)
    if not extracted.is_file():
        raise RuntimeError(f"Archive did not contain {ARCHIVE_ENTRY}")
    return extracted


def normalize_blacklist(raw: bytes) -> tuple[bytes, int, int]:
    values = json.loads(raw)
    if not isinstance(values, list) or any(not isinstance(value, str) for value in values):
        raise ValueError("BetterGI pickup blacklist must be a JSON string array")
    cleaned = [value for value in values if value.strip()]
    if not cleaned:
        raise ValueError("BetterGI pickup blacklist is empty after removing blank entries")
    encoded = json.dumps(
        cleaned, ensure_ascii=False, separators=(",", ":")
    ).encode("utf-8")
    return encoded, len(cleaned), len(set(cleaned))


def public_resource_url(notice_url: str, resource_key: str) -> str:
    notice = urllib.parse.urlsplit(notice_url)
    if notice.scheme not in {"http", "https"} or not notice.netloc:
        raise ValueError("QINIU_NOTICE_URL must be an absolute HTTP(S) URL")
    return urllib.parse.urlunsplit(
        ("https", notice.netloc, "/" + resource_key.lstrip("/"), "", "")
    )


def current_upstream_release(notice: dict[str, Any]) -> str | None:
    plugins = notice.get("plugins")
    if not isinstance(plugins, dict):
        return None
    plugin = plugins.get(PLUGIN_ID)
    if not isinstance(plugin, dict):
        return None
    resources = plugin.get("resources")
    if not isinstance(resources, dict):
        return None
    resource = resources.get(RESOURCE_KEY)
    return (
        str(resource.get("upstreamRelease"))
        if isinstance(resource, dict) and resource.get("upstreamRelease")
        else None
    )


def update_notice(
    notice: dict[str, Any],
    release_version: str,
    resource_bytes: bytes,
    resource_key: str,
    resource_url: str,
) -> tuple[dict[str, Any], bool, dict[str, Any]]:
    plugins = notice.get("plugins")
    if not isinstance(plugins, dict) or not isinstance(plugins.get(PLUGIN_ID), dict):
        raise ValueError(
            f"Existing notice must contain the published {PLUGIN_ID} plugin entry"
        )
    plugin = plugins[PLUGIN_ID]
    resources = plugin.get("resources")
    if not isinstance(resources, dict):
        resources = {}
        plugin["resources"] = resources
    previous = resources.get(RESOURCE_KEY)
    previous = previous if isinstance(previous, dict) else {}

    digest = hashlib.sha256(resource_bytes).hexdigest()
    values = json.loads(resource_bytes)
    resource_changed = previous.get("sha256", "").lower() != digest
    if resource_changed:
        resource = {
            "version": f"bettergi-{release_version}",
            "upstreamRelease": release_version,
            "minPluginVersion": "0.3.3",
            "fileName": "default_pick_black_lists.json",
            "size": len(resource_bytes),
            "sha256": digest,
            "entryCount": len(values),
            "url": resource_url,
        }
    else:
        resource = dict(previous)
        resource["upstreamRelease"] = release_version

    resources[RESOURCE_KEY] = resource
    notice["schemaVersion"] = max(int(notice.get("schemaVersion") or 1), 2)
    return notice, resource_changed, resource


def write_output(name: str, value: str) -> None:
    output_path = os.environ.get("GITHUB_OUTPUT")
    if output_path:
        with open(output_path, "a", encoding="utf-8") as output:
            output.write(f"{name}={value}\n")
    print(f"{name}={value}")


def run(args: argparse.Namespace) -> int:
    output_directory = Path(args.output_dir).resolve()
    output_directory.mkdir(parents=True, exist_ok=True)

    notice_payload, notice_headers = request_bytes(args.notice_url)
    notice = json.loads(notice_payload)
    old_etag = notice_headers.get("etag", "")

    release = load_latest_release(args.github_token)
    version = str(release["tag_name"]).removeprefix("v")
    current_release = current_upstream_release(notice)
    write_output("release_version", version)
    write_output("old_etag", old_etag)
    if current_release == version:
        write_output("changed", "false")
        write_output("resource_changed", "false")
        print(f"BetterGI {version} is already reflected in notice.json")
        return 0

    asset = select_archive_asset(release)
    archive_path = output_directory / str(asset["name"])
    actual_size, actual_digest = download_file(
        str(asset["browser_download_url"]), archive_path, args.github_token
    )
    expected_size = int(asset["size"])
    expected_digest = str(asset["digest"]).split(":", 1)[1].lower()
    if actual_size != expected_size:
        raise RuntimeError(
            f"BetterGI archive size mismatch: expected {expected_size}, got {actual_size}"
        )
    if actual_digest != expected_digest:
        raise RuntimeError(
            f"BetterGI archive SHA-256 mismatch: expected {expected_digest}, got {actual_digest}"
        )

    extracted = extract_blacklist(archive_path, output_directory, args.seven_zip)
    resource_bytes, entry_count, unique_count = normalize_blacklist(extracted.read_bytes())
    resource_key = f"{args.resource_prefix.strip('/')}/{version}.json"
    resource_url = public_resource_url(args.notice_url, resource_key)
    notice, resource_changed, resource = update_notice(
        notice, version, resource_bytes, resource_key, resource_url
    )

    resource_path = output_directory / f"{version}.json"
    resource_path.write_bytes(resource_bytes)
    notice_path = output_directory / "notice.json"
    notice_path.write_text(
        json.dumps(notice, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )

    write_output("changed", "true")
    write_output("resource_changed", str(resource_changed).lower())
    write_output("resource_key", resource_key)
    write_output("resource_url", resource_url)
    write_output("resource_file", str(resource_path))
    write_output("notice_file", str(notice_path))
    write_output("resource_sha256", str(resource["sha256"]))
    print(
        f"Prepared BetterGI {version}: {entry_count} entries, "
        f"{unique_count} unique, resource_changed={resource_changed}"
    )
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--notice-url", required=True)
    parser.add_argument("--resource-prefix", required=True)
    parser.add_argument("--output-dir", default="out/bettergi-sync")
    parser.add_argument("--seven-zip", default="7z")
    parser.add_argument("--github-token", default=os.environ.get("GITHUB_TOKEN"))
    return parser.parse_args()


if __name__ == "__main__":
    try:
        raise SystemExit(run(parse_args()))
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        raise
