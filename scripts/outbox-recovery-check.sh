#!/usr/bin/env bash
# Runs the real ERP migrations plus outbox claim/lease/backoff checks against a PostgreSQL server.
# Usage: PGHOST=... PGPORT=... PGUSER=... scripts/outbox-recovery-check.sh   (creates and drops database erp_outbox_check)
set -euo pipefail
cd "$(dirname "$0")/.."
DB=erp_outbox_check
psql -qc "drop database if exists $DB" -c "create database $DB" postgres
for f in erp/src/main/resources/db/migration/V*.sql; do psql -d $DB -q -v ON_ERROR_STOP=1 -f "$f" >/dev/null; done
out=$(psql -d $DB -At -f scripts/outbox-recovery-check.sql)
echo "$out"
for want in rows_after_duplicate\|120 claim1\|50 claim2\|50 overlap\|0 claimable_while_leased\|20 claimable_after_lease_expiry\|120 unavailable_during_backoff\|0; do
  grep -qx "$want" <<<"$out" || { echo "FAILED: expected $want"; exit 1; }
done
echo "outbox recovery check: OK"
psql -qc "drop database $DB" postgres
