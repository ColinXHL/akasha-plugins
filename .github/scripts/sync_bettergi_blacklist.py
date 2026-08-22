#!/usr/bin/env python3
"""Prepare and apply an independently versioned BetterGI blacklist update."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import Any

import requests


UPSTREAM_REPOSITORY = "babalae/better-genshin-impact"
PLUGIN_ID = "akasha-genshin-automation"
RESOURCE_ID = "bettergi-default-pick-blacklist"
UPSTREAM_FILE = "Assets/Config/Pick/default_pick_black_lists.json"
MINIMUM_ENTRY_COUNT = 1000
MAXIMUM_ENTRY_COUNT = 20000
MAXIMUM_ENTRY_LENGTH = 200


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as stream:
        return json.load(stream)


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")


def validate_blacklist(payload: bytes) -> list[str]:
    try:
        value = json.loads(payload.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"blacklist is not valid UTF-8 JSON: {error}") from error

    if not isinstance(value, list):
        raise ValueError("blacklist root must be an array")
    if not MINIMUM_ENTRY_COUNT <= len(value) <= MAXIMUM_ENTRY_COUNT:
        raise ValueError(
            f"blacklist entry count {len(value)} is outside "
            f"{MINIMUM_ENTRY_COUNT}..{MAXIMUM_ENTRY_COUNT}"
        )
    for index, entry in enumerate(value):
        if not isinstance(entry, str) or not entry.strip():
            raise ValueError(f"blacklist entry {index} must be a non-empty string")
        if len(entry) > MAXIMUM_ENTRY_LENGTH:
            raise ValueError(f"blacklist entry {index} is too long")
        if any(ord(character) < 32 for character in entry):
            raise ValueError(f"blacklist entry {index} contains control characters")
    return value


def select_release_asset(release: dict[str, Any]) -> dict[str, Any]:
    candidates = [
        asset
        for asset in release.get("assets", [])
        if isinstance(asset, dict)
        and isinstance(asset.get("name"), str)
        and asset["name"].lower().startswith("bettergi_v")
        and asset["name"].lower().endswith(".7z")
    ]
    if len(candidates) != 1:
        names = [asset.get("name") for asset in release.get("assets", [])]
        raise ValueError(
            "expected exactly one BetterGI_v*.7z Release asset; "
            f"found {len(candidates)} among {names}"
        )
    return candidates[0]


def find_archive_entry(archive: Path, seven_zip: str) -> str:
    result = subprocess.run(
        [seven_zip, "l", "-slt", str(archive)],
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    suffix = UPSTREAM_FILE.lower()
    matches: list[str] = []
    for line in result.stdout.splitlines():
        if not line.startswith("Path = "):
            continue
        candidate = line[7:].replace("\\", "/")
        normalized = str(PurePosixPath(candidate))
        if normalized.lower().endswith(suffix):
            matches.append(candidate)
    if len(matches) != 1:
        raise ValueError(
            f"expected exactly one {UPSTREAM_FILE} in archive; found {matches}"
        )
    return matches[0]


def extract_archive_entry(archive: Path, entry: str, seven_zip: str) -> bytes:
    result = subprocess.run(
        [seven_zip, "e", "-so", str(archive), entry],
        check=True,
        capture_output=True,
    )
    if not result.stdout:
        raise ValueError("extracted blacklist is empty")
    return result.stdout


def github_headers(token: str) -> dict[str, str]:
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": "Akasha-Plugins-Blacklist-Sync/1.0",
        "X-GitHub-Api-Version": "2022-11-28",
    }
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return headers


def numeric_version(value: str) -> tuple[int, ...] | None:
    normalized = value[1:] if value.lower().startswith("v") else value
    parts = normalized.split(".")
    if not parts or len(parts) > 4 or any(not part.isdigit() for part in parts):
        return None
    values = tuple(int(part) for part in parts)
    return values + (0,) * (4 - len(values))


def get_latest_stable_release(token: str) -> dict[str, Any]:
    url = f"https://api.github.com/repos/{UPSTREAM_REPOSITORY}/releases/latest"
    response = requests.get(url, headers=github_headers(token), timeout=(15, 60))
    response.raise_for_status()
    release = response.json()
    if release.get("draft") or release.get("prerelease"):
        raise ValueError("GitHub latest Release is not a published stable Release")
    if not release.get("tag_name"):
        raise ValueError("GitHub Release response has no tag_name")
    return release


def download_asset(asset: dict[str, Any], destination: Path, token: str) -> None:
    url = asset.get("browser_download_url")
    if not isinstance(url, str) or not url.startswith("https://"):
        raise ValueError("Release asset has no secure download URL")
    destination.parent.mkdir(parents=True, exist_ok=True)
    with requests.get(
        url,
        headers=github_headers(token),
        stream=True,
        timeout=(30, 900),
    ) as response:
        response.raise_for_status()
        with destination.open("wb") as stream:
            for chunk in response.iter_content(1024 * 1024):
                if chunk:
                    stream.write(chunk)
    expected_size = asset.get("size")
    if isinstance(expected_size, int) and destination.stat().st_size != expected_size:
        raise ValueError("downloaded BetterGI archive size does not match GitHub")


def published_resource_matches(
    published_catalog_path: Path | None,
    current_resource: dict[str, Any],
) -> bool:
    if published_catalog_path is None or not published_catalog_path.is_file():
        return False
    try:
        published = load_json(published_catalog_path)
        matches = [
            item
            for item in published.get("resources", [])
            if item.get("id") == RESOURCE_ID
        ]
        return len(matches) == 1 and matches[0] == current_resource
    except (OSError, json.JSONDecodeError, AttributeError):
        return False


def public_mirrors_available(resource: dict[str, Any], token: str) -> bool:
    distribution = resource.get("distribution", {})
    tag = distribution.get("tag")
    asset = distribution.get("asset")
    expected_hash = distribution.get("sha256")
    expected_size = distribution.get("size")
    if (
        not isinstance(tag, str)
        or not isinstance(asset, str)
        or not isinstance(expected_hash, str)
        or not isinstance(expected_size, int)
    ):
        return False
    urls = [
        f"https://github.com/ColinXHL/akasha-plugins/releases/download/{tag}/{asset}",
        f"https://cnb.cool/AkashaNavigator/akasha-plugins/-/releases/download/{tag}/{asset}",
    ]
    for url in urls:
        try:
            with requests.get(
                url,
                headers=github_headers(token) if "github.com" in url else {
                    "User-Agent": "Akasha-Plugins-Blacklist-Sync/1.0"
                },
                stream=True,
                allow_redirects=True,
                timeout=(15, 60),
            ) as response:
                if not response.ok:
                    return False
                digest = hashlib.sha256()
                received = 0
                for chunk in response.iter_content(64 * 1024):
                    if not chunk:
                        continue
                    received += len(chunk)
                    if received > expected_size:
                        return False
                    digest.update(chunk)
                if received != expected_size or digest.hexdigest() != expected_hash:
                    return False
        except requests.RequestException:
            return False
    return True


def build_plan(
    resource_catalog: dict[str, Any],
    release: dict[str, Any],
    payload: bytes,
    asset_directory: Path,
) -> dict[str, Any]:
    entries = validate_blacklist(payload)
    resources = resource_catalog.get("resources", [])
    matches = [item for item in resources if item.get("id") == RESOURCE_ID]
    if len(matches) != 1:
        raise ValueError(f"resource catalog must contain exactly one {RESOURCE_ID}")

    current = matches[0]
    tag_name = str(release["tag_name"])
    source_version = tag_name[1:] if tag_name.lower().startswith("v") else tag_name
    current_version = numeric_version(str(current.get("sourceVersion", ""))) if current else None
    latest_version = numeric_version(source_version)
    if (
        current_version is not None
        and latest_version is not None
        and latest_version < current_version
    ):
        append_output("hasUpdate", False)
        print(
            f"Ignoring apparent BetterGI rollback from {current['sourceVersion']} "
            f"to {source_version}"
        )
        return
    digest = hashlib.sha256(payload).hexdigest()
    asset_name = f"default_pick_black_lists.{digest[:12]}.json"
    resource_tag = f"{PLUGIN_ID}-resource-{RESOURCE_ID}-{digest[:12]}"
    asset_path = asset_directory / asset_name
    asset_directory.mkdir(parents=True, exist_ok=True)
    asset_path.write_bytes(payload)
    (asset_directory / f"{asset_name}.sha256").write_text(
        f"{digest}  {asset_name}\n",
        encoding="ascii",
    )

    updated_catalog = json.loads(json.dumps(resource_catalog))
    updated_resource = next(
        item for item in updated_catalog["resources"] if item["id"] == RESOURCE_ID
    )
    updated_resource.update(
        {
            "revision": digest,
            "sourceVersion": source_version,
            "optional": True,
            "distribution": {
                "type": "release",
                "tag": resource_tag,
                "asset": asset_name,
                "size": len(payload),
                "sha256": digest,
            },
        }
    )
    return {
        "schemaVersion": 1,
        "upstreamRepository": UPSTREAM_REPOSITORY,
        "upstreamReleaseTag": tag_name,
        "upstreamReleaseUrl": release.get("html_url", ""),
        "upstreamAsset": select_release_asset(release)["name"],
        "entryCount": len(entries),
        "resourceChanged": current.get("revision") != digest,
        "metadataChanged": updated_resource != current,
        "resourceTag": resource_tag,
        "assetName": asset_name,
        "assetPath": str(asset_path),
        "size": len(payload),
        "sha256": digest,
        "updatedCatalog": updated_catalog,
    }


def append_output(name: str, value: Any) -> None:
    output_path = os.environ.get("GITHUB_OUTPUT")
    if not output_path:
        return
    with Path(output_path).open("a", encoding="utf-8", newline="\n") as stream:
        stream.write(f"{name}={str(value).lower() if isinstance(value, bool) else value}\n")


def command_prepare(args: argparse.Namespace) -> None:
    root = args.repository_root.resolve()
    catalog_path = root / "plugins" / PLUGIN_ID / "resources.json"
    resource_catalog = load_json(catalog_path)
    release = get_latest_stable_release(args.github_token)
    resources = resource_catalog.get("resources", [])
    current = next(
        (item for item in resources if item.get("id") == RESOURCE_ID),
        None,
    )
    tag_name = str(release["tag_name"])
    source_version = tag_name[1:] if tag_name.startswith("v") else tag_name
    if current is not None and current.get("sourceVersion") == source_version and not args.force:
        catalog_matches = published_resource_matches(args.published_catalog, current)
        mirrors_available = public_mirrors_available(current, args.github_token)
        if catalog_matches and mirrors_available:
            append_output("hasUpdate", False)
            print(
                f"BetterGI {tag_name}, catalog metadata, and both mirrors "
                "are already synchronized"
            )
            return
        print(
            "Retrying current BetterGI Release because catalog metadata or "
            "a public mirror has not converged"
        )

    append_output("hasUpdate", True)
    asset = select_release_asset(release)
    archive = args.output_directory / asset["name"]
    download_asset(asset, archive, args.github_token)
    entry = find_archive_entry(archive, args.seven_zip)
    payload = extract_archive_entry(archive, entry, args.seven_zip)
    plan = build_plan(resource_catalog, release, payload, args.output_directory)
    write_json(args.plan, plan)
    for key in (
        "resourceChanged",
        "metadataChanged",
        "resourceTag",
        "assetName",
        "assetPath",
        "size",
        "sha256",
    ):
        append_output(key, plan[key])
    print(
        f"Prepared BetterGI {plan['upstreamReleaseTag']}: "
        f"{plan['entryCount']} entries, sha256 {plan['sha256']}"
    )


def command_apply(args: argparse.Namespace) -> None:
    root = args.repository_root.resolve()
    plan = load_json(args.plan)
    if plan.get("schemaVersion") != 1:
        raise ValueError("unsupported sync plan schemaVersion")
    catalog_path = root / "plugins" / PLUGIN_ID / "resources.json"
    write_json(catalog_path, plan["updatedCatalog"])
    history_path = root / "plugins" / PLUGIN_ID / "resources" / "bettergi-blacklist-history.json"
    history = load_json(history_path) if history_path.is_file() else {
        "schemaVersion": 1,
        "resourceId": RESOURCE_ID,
        "updates": [],
    }
    updated_resource = next(
        item
        for item in plan["updatedCatalog"]["resources"]
        if item["id"] == RESOURCE_ID
    )
    update = {
        "sourceVersion": updated_resource["sourceVersion"],
        "upstreamReleaseTag": plan["upstreamReleaseTag"],
        "upstreamReleaseUrl": plan["upstreamReleaseUrl"],
        "upstreamAsset": plan["upstreamAsset"],
        "entryCount": plan["entryCount"],
        "size": plan["size"],
        "sha256": plan["sha256"],
        "syncedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    if (
        not history["updates"]
        or history["updates"][-1].get("upstreamReleaseTag") != plan["upstreamReleaseTag"]
        or history["updates"][-1].get("sha256") != plan["sha256"]
    ):
        history["updates"].append(update)
    write_json(history_path, history)
    print(f"Applied resource catalog update for {plan['upstreamReleaseTag']}")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    prepare = subparsers.add_parser("prepare")
    prepare.add_argument("--repository-root", type=Path, default=Path.cwd())
    prepare.add_argument("--output-directory", type=Path, required=True)
    prepare.add_argument("--plan", type=Path, required=True)
    prepare.add_argument("--github-token", default="")
    prepare.add_argument("--seven-zip", default="7z")
    prepare.add_argument("--published-catalog", type=Path)
    prepare.add_argument("--force", action="store_true")
    prepare.set_defaults(handler=command_prepare)
    apply = subparsers.add_parser("apply")
    apply.add_argument("--repository-root", type=Path, default=Path.cwd())
    apply.add_argument("--plan", type=Path, required=True)
    apply.set_defaults(handler=command_apply)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    args.handler(args)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"BetterGI blacklist sync failed: {error}", file=sys.stderr)
        raise SystemExit(1)
