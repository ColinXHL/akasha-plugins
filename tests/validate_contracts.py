#!/usr/bin/env python3
"""Validate the frozen Phase 0 JSON contracts and their examples."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator
from referencing import Registry, Resource


ROOT = Path(__file__).resolve().parents[1]
SCHEMA_DIRECTORY = ROOT / "schemas"
EXAMPLE_DIRECTORY = SCHEMA_DIRECTORY / "examples"


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as stream:
        return json.load(stream)


def build_registry(*schemas: dict[str, Any]) -> Registry:
    registry = Registry()
    for schema in schemas:
        registry = registry.with_resource(
            schema["$id"],
            Resource.from_contents(schema),
        )
    return registry


def validate_index_contract(index: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    plugins = index["plugins"]
    ids = [plugin["id"] for plugin in plugins]

    if len(ids) != len(set(ids)):
        errors.append("plugin IDs must be unique")
    if ids != sorted(ids):
        errors.append("plugins must be sorted by id")

    for plugin in plugins:
        expected_path = f"plugins/{plugin['id']}"
        if plugin["path"] != expected_path:
            errors.append(
                f"{plugin['id']}: path must be {expected_path!r}, "
                f"got {plugin['path']!r}"
            )

    return errors


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


def validate_inventory() -> list[str]:
    inventory_path = (
        ROOT / "docs" / "migration" / "phase-0-plugin-inventory.json"
    )
    inventory = load_json(inventory_path)
    errors: list[str] = []
    plugins = inventory.get("plugins", [])
    ids = [plugin.get("id") for plugin in plugins]

    if inventory.get("schemaVersion") != 1:
        errors.append("inventory schemaVersion must be 1")
    if len(plugins) != 4:
        errors.append("inventory must contain exactly the four migration plugins")
    if len(ids) != len(set(ids)):
        errors.append("inventory plugin IDs must be unique")
    if ids != sorted(ids):
        errors.append("inventory plugins must be sorted by id")

    for plugin in plugins:
        plugin_id = plugin.get("id")
        if plugin.get("targetPath") != f"plugins/{plugin_id}":
            errors.append(f"{plugin_id}: inventory targetPath is invalid")

    expected_versions = {
        "akasha-genshin-automation": "0.4.3",
        "bilibili-page-list": "1.2.1",
        "genshin-direction-marker": "1.1.0",
        "smart-cursor-detection": "1.0.0",
    }
    actual_versions = {
        plugin.get("id"): plugin.get("baselineVersion")
        for plugin in plugins
    }
    if actual_versions != expected_versions:
        errors.append("inventory baseline versions do not match the Phase 0 audit")

    return errors


def main() -> int:
    manifest_schema = load_json(SCHEMA_DIRECTORY / "plugin-manifest.schema.json")
    index_schema = load_json(SCHEMA_DIRECTORY / "repository-index.schema.json")
    Draft202012Validator.check_schema(manifest_schema)
    Draft202012Validator.check_schema(index_schema)

    registry = build_registry(manifest_schema, index_schema)
    manifest_validator = Draft202012Validator(
        manifest_schema,
        registry=registry,
    )
    index_validator = Draft202012Validator(
        index_schema,
        registry=registry,
    )

    failures: list[str] = []
    valid_directory = EXAMPLE_DIRECTORY / "valid"
    for path in sorted(valid_directory.glob("*.manifest.json")):
        instance = load_json(path)
        schema_errors = sorted(
            manifest_validator.iter_errors(instance),
            key=lambda error: list(error.absolute_path),
        )
        failures.extend(f"{path}: {error.message}" for error in schema_errors)
        failures.extend(
            f"{path}: {error}" for error in validate_release_naming(instance)
        )

    valid_index_path = valid_directory / "repo.json"
    valid_index = load_json(valid_index_path)
    failures.extend(
        f"{valid_index_path}: {error.message}"
        for error in index_validator.iter_errors(valid_index)
    )
    failures.extend(
        f"{valid_index_path}: {error}"
        for error in validate_index_contract(valid_index)
    )

    invalid_directory = EXAMPLE_DIRECTORY / "invalid"
    invalid_manifest_count = 0
    for path in sorted(invalid_directory.glob("*.manifest.json")):
        invalid_manifest_count += 1
        instance = load_json(path)
        if not list(manifest_validator.iter_errors(instance)):
            failures.append(f"{path}: invalid manifest unexpectedly passed")

    invalid_index_path = invalid_directory / "duplicate-id.repo.json"
    invalid_index = load_json(invalid_index_path)
    schema_errors = list(index_validator.iter_errors(invalid_index))
    contract_errors = validate_index_contract(invalid_index)
    if not schema_errors and not contract_errors:
        failures.append(
            f"{invalid_index_path}: invalid repository index unexpectedly passed"
        )

    failures.extend(
        f"phase-0-plugin-inventory.json: {error}"
        for error in validate_inventory()
    )

    if failures:
        print("Contract validation failed:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1

    print("Contract validation passed.")
    print(
        "Validated 2 schemas, 3 valid examples, "
        f"{invalid_manifest_count + 1} invalid examples, and the plugin inventory."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
