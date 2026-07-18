#!/usr/bin/env python3
"""Validate plugin manifests, generate repo.json, and stage catalog content."""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

from jsonschema import Draft202012Validator


EXCLUDED_DIRECTORY_NAMES = {
    ".git",
    ".github",
    "artifacts",
    "backend",
    "bin",
    "build",
    "dist",
    "node_modules",
    "obj",
    "packaging",
    "publish",
    "tests",
}
EXCLUDED_FILE_SUFFIXES = {
    ".dll",
    ".exe",
    ".pdb",
    ".zip",
}


@dataclass(frozen=True)
class PluginRecord:
    directory: Path
    manifest_path: Path
    manifest: dict[str, Any]


class RepositoryValidationError(Exception):
    """Raised when one or more repository contracts are invalid."""

    def __init__(self, messages: Iterable[str]):
        self.messages = list(messages)
        super().__init__("\n".join(self.messages))


def load_json(path: Path) -> dict[str, Any]:
    try:
        with path.open("r", encoding="utf-8") as stream:
            value = json.load(stream)
    except (OSError, json.JSONDecodeError) as error:
        raise RepositoryValidationError([f"{path}: {error}"]) from error

    if not isinstance(value, dict):
        raise RepositoryValidationError([f"{path}: root value must be an object"])
    return value


def is_contained_path(root: Path, candidate: Path) -> bool:
    try:
        candidate.resolve().relative_to(root.resolve())
    except ValueError:
        return False
    return True


def resolve_plugin_path(plugin_directory: Path, relative_path: str) -> Path:
    candidate = plugin_directory.joinpath(*relative_path.split("/"))
    if not is_contained_path(plugin_directory, candidate):
        raise RepositoryValidationError(
            [f"{plugin_directory}: path escapes plugin directory: {relative_path}"]
        )
    return candidate


def create_manifest_validator(root: Path) -> Draft202012Validator:
    schema_path = root / "schemas" / "plugin-manifest.schema.json"
    schema = load_json(schema_path)
    Draft202012Validator.check_schema(schema)
    return Draft202012Validator(schema)


def validate_release_naming(manifest: dict[str, Any]) -> list[str]:
    distribution = manifest["distribution"]
    if distribution["type"] != "release":
        return []

    plugin_id = manifest["id"]
    version = manifest["version"]
    expected_tag = f"{plugin_id}-v{version}"
    expected_asset = f"{plugin_id}-{version}-win-x64.zip"
    errors: list[str] = []
    if distribution["tag"] != expected_tag:
        errors.append(
            f"{plugin_id}: release tag must be {expected_tag!r}"
        )
    if distribution["asset"] != expected_asset:
        errors.append(
            f"{plugin_id}: release asset must be {expected_asset!r}"
        )
    return errors


def validate_manifest_files(record: PluginRecord) -> list[str]:
    manifest = record.manifest
    errors: list[str] = []
    file_fields = ["main"]
    if "settings" in manifest:
        file_fields.append("settings")

    for field in file_fields:
        candidate = resolve_plugin_path(record.directory, manifest[field])
        if not candidate.is_file():
            errors.append(
                f"{record.manifest_path}: {field} file does not exist: "
                f"{manifest[field]}"
            )

    readme_path = record.directory / "README.md"
    if not readme_path.is_file():
        errors.append(f"{record.directory}: README.md does not exist")

    backend = manifest.get("backend")
    if backend and manifest["distribution"]["type"] != "release":
        errors.append(
            f"{record.manifest_path}: backend requires release distribution"
        )

    if manifest.get("httpAllowedUrls") and "network" not in manifest["permissions"]:
        errors.append(
            f"{record.manifest_path}: httpAllowedUrls requires network permission"
        )

    return errors


def discover_plugins(
    root: Path,
    *,
    require_release_integrity: bool,
) -> list[PluginRecord]:
    plugin_root = root / "plugins"
    if not plugin_root.exists():
        return []
    if not plugin_root.is_dir():
        raise RepositoryValidationError([f"{plugin_root}: must be a directory"])

    validator = create_manifest_validator(root)
    records: list[PluginRecord] = []
    errors: list[str] = []
    seen_ids: dict[str, Path] = {}

    for plugin_directory in sorted(
        (path for path in plugin_root.iterdir() if path.is_dir()),
        key=lambda path: path.name,
    ):
        manifest_path = plugin_directory / "manifest.json"
        if not manifest_path.is_file():
            errors.append(f"{plugin_directory}: manifest.json does not exist")
            continue

        try:
            manifest = load_json(manifest_path)
        except RepositoryValidationError as error:
            errors.extend(error.messages)
            continue

        for error in sorted(
            validator.iter_errors(manifest),
            key=lambda item: list(item.absolute_path),
        ):
            location = ".".join(str(value) for value in error.absolute_path)
            suffix = f" at {location}" if location else ""
            errors.append(f"{manifest_path}: {error.message}{suffix}")

        plugin_id = manifest.get("id")
        if not isinstance(plugin_id, str):
            continue
        if plugin_directory.name != plugin_id:
            errors.append(
                f"{manifest_path}: directory must be named {plugin_id!r}"
            )
        if plugin_id in seen_ids:
            errors.append(
                f"{manifest_path}: duplicate plugin ID also declared by "
                f"{seen_ids[plugin_id]}"
            )
        else:
            seen_ids[plugin_id] = manifest_path

        if require_release_integrity and manifest.get("distribution", {}).get(
            "type"
        ) == "release":
            distribution = manifest["distribution"]
            if "sha256" not in distribution or "size" not in distribution:
                errors.append(
                    f"{manifest_path}: catalog release manifest requires "
                    "sha256 and size"
                )

        try:
            errors.extend(validate_manifest_files(
                PluginRecord(plugin_directory, manifest_path, manifest)
            ))
            errors.extend(
                f"{manifest_path}: {message}"
                for message in validate_release_naming(manifest)
            )
        except RepositoryValidationError as error:
            errors.extend(error.messages)

        records.append(PluginRecord(plugin_directory, manifest_path, manifest))

    if errors:
        raise RepositoryValidationError(errors)

    return sorted(records, key=lambda record: record.manifest["id"])


def build_index(records: list[PluginRecord], source_commit: str) -> dict[str, Any]:
    return {
        "schemaVersion": 1,
        "commit": source_commit,
        "plugins": [
            {
                "id": record.manifest["id"],
                "path": f"plugins/{record.manifest['id']}",
                "name": record.manifest["name"],
                "version": record.manifest["version"],
                "description": record.manifest["description"],
                "distributionType": record.manifest["distribution"]["type"],
                "hasBackend": "backend" in record.manifest,
                "minHostVersion": record.manifest["host"]["minVersion"],
            }
            for record in records
        ],
    }


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")


def should_copy_repository_file(plugin_directory: Path, path: Path) -> bool:
    relative = path.relative_to(plugin_directory)
    if any(part in EXCLUDED_DIRECTORY_NAMES for part in relative.parts[:-1]):
        return False
    if path.suffix.lower() in EXCLUDED_FILE_SUFFIXES:
        return False
    return path.name not in {".DS_Store", "Thumbs.db"}


def copy_plugin_to_catalog(record: PluginRecord, target: Path) -> None:
    distribution_type = record.manifest["distribution"]["type"]
    target.mkdir(parents=True, exist_ok=True)

    if distribution_type == "release":
        for file_name in ("manifest.json", "README.md"):
            shutil.copy2(record.directory / file_name, target / file_name)
        return

    for source in sorted(record.directory.rglob("*")):
        if not source.is_file() or not should_copy_repository_file(
            record.directory,
            source,
        ):
            continue
        relative = source.relative_to(record.directory)
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)


def reset_staging_directory(root: Path, output: Path) -> None:
    resolved_root = root.resolve()
    resolved_output = output.resolve()
    if resolved_output == resolved_root or not is_contained_path(
        resolved_root,
        resolved_output,
    ):
        raise RepositoryValidationError(
            [f"catalog output must stay inside repository: {output}"]
        )
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)


def command_validate(args: argparse.Namespace) -> None:
    records = discover_plugins(
        args.root,
        require_release_integrity=args.require_release_integrity,
    )
    print(f"Validated {len(records)} plugin manifest(s).")


def command_generate(args: argparse.Namespace) -> None:
    records = discover_plugins(
        args.root,
        require_release_integrity=args.require_release_integrity,
    )
    write_json(args.output, build_index(records, args.commit))
    print(f"Generated {args.output} with {len(records)} plugin(s).")


def command_stage(args: argparse.Namespace) -> None:
    records = discover_plugins(
        args.root,
        require_release_integrity=True,
    )
    reset_staging_directory(args.root, args.output)
    for record in records:
        copy_plugin_to_catalog(
            record,
            args.output / "plugins" / record.manifest["id"],
        )
    shutil.copy2(args.root / "LICENSE", args.output / "LICENSE")
    write_json(args.output / "repo.json", build_index(records, args.commit))
    print(f"Staged catalog at {args.output} with {len(records)} plugin(s).")


def commit_sha(value: str) -> str:
    normalized = value.lower()
    if len(normalized) not in {40, 64} or any(
        character not in "0123456789abcdef" for character in normalized
    ):
        raise argparse.ArgumentTypeError(
            "commit must be a 40- or 64-character hexadecimal Git SHA"
        )
    return normalized


def path_value(value: str) -> Path:
    return Path(value).resolve()


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--root",
        type=path_value,
        default=Path.cwd(),
        help="AkashaPlugins repository root",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate = subparsers.add_parser("validate")
    validate.add_argument("--require-release-integrity", action="store_true")
    validate.set_defaults(handler=command_validate)

    generate = subparsers.add_parser("generate")
    generate.add_argument("--commit", required=True, type=commit_sha)
    generate.add_argument("--output", required=True, type=path_value)
    generate.add_argument("--require-release-integrity", action="store_true")
    generate.set_defaults(handler=command_generate)

    stage = subparsers.add_parser("stage")
    stage.add_argument("--commit", required=True, type=commit_sha)
    stage.add_argument("--output", required=True, type=path_value)
    stage.set_defaults(handler=command_stage)
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    try:
        args.handler(args)
    except RepositoryValidationError as error:
        print("Repository validation failed:", file=sys.stderr)
        for message in error.messages:
            print(f"- {message}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
