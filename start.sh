#!/usr/bin/env bash
# One-command local infrastructure: CRM DB, ERP DB, Temporal (+UI), Keycloak.
set -euo pipefail
cd "$(dirname "$0")"
[ -f .env ] || cp .env.example .env
docker compose --env-file .env -f infrastructure/docker-compose.yml up -d --wait
echo "CRM DB :5433 | ERP DB :5434 | Temporal :7233 (UI http://localhost:8233) | Keycloak http://localhost:8080"
