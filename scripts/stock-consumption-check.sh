#!/usr/bin/env bash
# Runs the real ERP migrations (V1-V6) and checks the stock-consumption SQL against a PostgreSQL server.
# Usage: PGHOST=... PGPORT=... PGUSER=... scripts/stock-consumption-check.sh   (creates and drops database erp_stock_check)
set -euo pipefail
cd "$(dirname "$0")/.."
DB=erp_stock_check
psql -qc "drop database if exists $DB" -c "create database $DB" postgres
trap 'psql -qc "drop database if exists $DB" postgres >/dev/null' EXIT
for f in erp/src/main/resources/db/migration/V*.sql; do psql -d $DB -q -v ON_ERROR_STOP=1 -f "$f" >/dev/null; done
out=$(psql -d $DB -At -f scripts/stock-consumption-check.sql 2>&1)
echo "$out"
for want in 'consumed_first_pass|1' 'consumed_second_pass|0' 'SKU-X_on_hand_reserved|6,0' \
            'cancel_releasable_rows_after_consume|0' 'cancel_releasable_rows_unshipped|1' 'SKU-Y_on_hand_reserved|10,0' \
            'OK: negative reserved rejected' 'OK: released+consumed rejected'; do
  grep -qF "$want" <<<"$out" || { echo "FAILED: expected '$want'"; exit 1; }
done
echo "stock consumption check: OK"
