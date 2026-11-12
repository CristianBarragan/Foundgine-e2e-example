#!/usr/bin/env bash
# Renders every diagrams/src/*.puml to diagrams/*.svg (and PNG with --png).
# Needs Java and Graphviz (`dot`). PlantUML: `plantuml` on PATH, or PLANTUML_JAR=/path/plantuml.jar.
set -euo pipefail
cd "$(dirname "$0")/diagrams/src"
fmt="-tsvg"; [ "${1:-}" = "--png" ] && fmt="-tpng"
if command -v plantuml >/dev/null 2>&1; then run=(plantuml)
elif [ -n "${PLANTUML_JAR:-}" ]; then run=(java -Djava.awt.headless=true -jar "$PLANTUML_JAR")
else echo "Install plantuml or set PLANTUML_JAR" >&2; exit 1; fi
files=$(ls [0-9]*.puml)
"${run[@]}" "$fmt" -o .. $files
echo "Rendered $(echo "$files" | wc -l) diagrams into docs/architecture/diagrams/"
