#!/usr/bin/env bash

set -euo pipefail

SRC=${1:?usage: pack-foundgine.sh <path-to-Foundgine-repo> <version>}
VERSION=${2:?usage: pack-foundgine.sh <path-to-Foundgine-repo> <version>}

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$(cd "$SRC" && pwd)"
OUT="$ROOT/nuget-local"

EXPECTED_COMMIT="dd18ccf9fd05cca927cfb5224d415f9726727bae"

ACTUAL_COMMIT=$(git -C "$SRC" rev-parse HEAD)

if [[ "$ACTUAL_COMMIT" != "$EXPECTED_COMMIT" ]]; then
    echo "ERROR: Unexpected Foundgine source commit."
    echo "Expected: $EXPECTED_COMMIT"
    echo "Actual:   $ACTUAL_COMMIT"
    exit 1
fi

EXPECTED_VERSION="3.3.6-ci.${ACTUAL_COMMIT:0:12}"

if [[ "$VERSION" != "$EXPECTED_VERSION" ]]; then
    echo "ERROR: Unexpected package version."
    echo "Expected: $EXPECTED_VERSION"
    echo "Actual:   $VERSION"
    exit 1
fi

echo "Foundgine source:  $ACTUAL_COMMIT"
echo "Package version:  $VERSION"
echo "Package output:   $OUT"

mkdir -p "$OUT"

rm -f "$OUT"/*.nupkg "$OUT"/*.snupkg

PROJECTS=(
    Foundgine.Core
    Foundgine.Runtime
    Foundgine.Providers
    Foundgine.Providers.AgentFramework
    Foundgine.Extensions
)

for PROJECT in "${PROJECTS[@]}"; do
    CSPROJ="$SRC/src/csharp/$PROJECT/$PROJECT.csproj"

    if [[ ! -f "$CSPROJ" ]]; then
        echo "ERROR: Project not found: $CSPROJ"
        exit 1
    fi

    echo
    echo "Packing $PROJECT/$VERSION"

    dotnet pack "$CSPROJ" \
        --configuration Release \
        --output "$OUT" \
        -p:PackageVersion="$VERSION"
done

echo
echo "Verifying generated NuGet packages..."

python3 - "$OUT" "$VERSION" <<'PY'
import pathlib
import sys
import zipfile
import xml.etree.ElementTree as ET

output = pathlib.Path(sys.argv[1])
expected_version = sys.argv[2]

expected_ids = {
    "Foundgine.Core",
    "Foundgine.Runtime",
    "Foundgine.Providers",
    "Foundgine.Providers.AgentFramework",
    "Foundgine.Extensions",
}

packages = {}

for package_file in output.glob("*.nupkg"):
    with zipfile.ZipFile(package_file) as archive:
        nuspecs = [
            name for name in archive.namelist()
            if name.lower().endswith(".nuspec")
        ]

        if len(nuspecs) != 1:
            raise SystemExit(
                f"{package_file.name}: expected exactly one nuspec, "
                f"found {len(nuspecs)}"
            )

        root = ET.fromstring(archive.read(nuspecs[0]))

        metadata = next(
            element for element in root
            if element.tag.endswith("metadata")
        )

        package_id = next(
            element.text for element in metadata
            if element.tag.endswith("id")
        )

        version = next(
            element.text for element in metadata
            if element.tag.endswith("version")
        )

        if package_id in packages:
            raise SystemExit(f"Duplicate package: {package_id}")

        if version != expected_version:
            raise SystemExit(
                f"{package_id}: expected {expected_version}, got {version}"
            )

        packages[package_id] = package_file

missing = expected_ids - packages.keys()

if missing:
    raise SystemExit(f"Missing packages: {sorted(missing)}")

for package_id in sorted(expected_ids):
    print(
        f"OK: {package_id}/{expected_version} "
        f"({packages[package_id].name})"
    )

print(f"Verified all {len(expected_ids)} required packages.")
print(f"Total packages produced: {len(packages)}")
PY

echo
echo "Foundgine package build and verification succeeded."
