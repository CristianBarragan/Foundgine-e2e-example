#!/usr/bin/env python3
"""Validate every contracts/examples/*.json fixture against its schema (refs resolved locally, never over the network).

Fixture -> schema mapping is by file name: <name>.v1.json -> schemas/<name>.v1.json, with explicit overrides below
for fixtures that exercise one branch of a oneOf schema. Run from anywhere; exit code 1 on any failure.
Requires: pip install jsonschema referencing
"""
import json
import pathlib
import sys

from jsonschema import Draft202012Validator
from referencing import Registry, Resource
from referencing.jsonschema import DRAFT202012

ROOT = pathlib.Path(__file__).resolve().parent.parent / "contracts"
SCHEMAS = ROOT / "schemas"
EXAMPLES = ROOT / "examples"

OVERRIDES = {
    "erp-event-shipment.v1.json": "erp-temporal-fulfilment-event.v1.json",
    "erp-event-invoice.v1.json": "erp-temporal-fulfilment-event.v1.json",
    "erp-event-payment.v1.json": "erp-temporal-fulfilment-event.v1.json",
    "quote-to-order-result-failed.v1.json": "quote-to-order-result.v1.json",
}


def build_registry() -> Registry:
    """Register every schema under its file name, its own $id, and under each sibling's $id directory, so a relative
    $ref ("quote-accepted.v1.json") resolves locally whichever base URI the referring schema declares."""
    docs = {p.name: json.loads(p.read_text()) for p in SCHEMAS.glob("*.json")}
    bases = {d["$id"].rsplit("/", 1)[0] for d in docs.values() if "$id" in d}
    registry = Registry()
    for name, doc in docs.items():
        resource = Resource.from_contents(doc, default_specification=DRAFT202012)
        uris = {name, *(f"{base}/{name}" for base in bases)}
        if "$id" in doc:
            uris.add(doc["$id"])
        for uri in uris:
            registry = registry.with_resource(uri, resource)
    return registry


def main() -> int:
    registry = build_registry()
    failures = 0
    for example in sorted(EXAMPLES.glob("*.json")):
        schema_name = OVERRIDES.get(example.name, example.name)
        schema_path = SCHEMAS / schema_name
        if not schema_path.exists():
            print(f"SKIP  {example.name} (no schema {schema_name})")
            continue
        validator = Draft202012Validator(json.loads(schema_path.read_text()), registry=registry,
                                         format_checker=Draft202012Validator.FORMAT_CHECKER)
        errors = sorted(validator.iter_errors(json.loads(example.read_text())), key=lambda e: list(e.path))
        if errors:
            failures += 1
            print(f"FAIL  {example.name} against {schema_name}")
            for e in errors:
                print(f"      {'/'.join(map(str, e.path)) or '<root>'}: {e.message}")
        else:
            print(f"ok    {example.name} against {schema_name}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
