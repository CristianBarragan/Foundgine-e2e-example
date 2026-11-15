#!/usr/bin/env bash
# Temporary bridge: packs Foundgine <version> from a local checkout of the Foundgine repository into ./nuget-local,
# for any package/feature that is not (yet) available on nuget.org. nuget.config lists nuget-local first, so the
# Docker builds and local builds pick the packed copy up. Delete the produced files once nuget.org has the version.
#   scripts/pack-foundgine.sh /path/to/Foundgine [3.3.5]
set -euo pipefail
SRC=${1:?usage: pack-foundgine.sh <path-to-Foundgine-repo> [version]}
VERSION=${2:-3.3.5}
OUT="$(cd "$(dirname "$0")/.." && pwd)/nuget-local"
mkdir -p "$OUT"
for proj in Foundgine.Core Foundgine.Runtime Foundgine.Providers Foundgine.Providers.AgentFramework Foundgine.Extensions; do
  dotnet pack "$SRC/src/csharp/$proj/$proj.csproj" -c Release -p:VersionPrefix="$VERSION" -o "$OUT"
done
ls -1 "$OUT" | grep "$VERSION"
