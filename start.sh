#!/usr/bin/env bash
# One-command local infrastructure: CRM DB (SQL Server), ERP DB (PostgreSQL), Temporal (+UI), Keycloak.
set -euo pipefail
cd "$(dirname "$0")"
[ -f .env ] || cp .env.example .env
docker compose --env-file .env -f infrastructure/docker-compose.yml up -d --wait
echo "CRM DB (SQL Server) :1433 | ERP DB (PostgreSQL) :5434 | Temporal :7233 (UI http://localhost:8233) | Keycloak http://localhost:8080"
