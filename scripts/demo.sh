#!/usr/bin/env bash
# Build and start the whole platform in containers, run the end-to-end journey, then check the parent workflow in Temporal.
# Usage: scripts/demo.sh [--keep]   (--keep leaves the stack running afterwards)
set -euo pipefail
cd "$(dirname "$0")/.."
[ -f .env ] || cp .env.example .env
DC=(docker compose --env-file .env -f infrastructure/docker-compose.yml -f infrastructure/docker-compose.demo.yml)

cleanup() { [ "${1:-}" = --keep ] || "${DC[@]}" down -v >/dev/null 2>&1 || true; }
trap 'rc=$?; [ $rc -eq 0 ] || "${DC[@]}" logs --no-color --tail=80 crm-api crm-worker erp orchestration temporal || true; cleanup "${1:-}"; exit $rc' EXIT

"${DC[@]}" up -d --build --wait
out=$("${DC[@]}" --profile e2e run --rm e2e | tee /dev/stderr)
quote=$(sed -n 's/^E2E_OK quoteId=\([0-9]*\) .*/\1/p' <<<"$out" | tail -1)
[ -n "$quote" ] || { echo "runner did not report success"; exit 1; }

echo "== Temporal: CustomerToCash:acme:$quote must have Completed"
desc=$("${DC[@]}" exec -T temporal tctl --address temporal:7233 --namespace enterprise workflow describe -w "CustomerToCash:acme:$quote")
echo "$desc" | grep -Ei 'status' || true
echo "$desc" | grep -Eqi 'Completed' || { echo "parent workflow did not complete"; exit 1; }
echo "DEMO OK"
