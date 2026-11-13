#!/usr/bin/env bash
# Exercises e2e/run.sh against the mock stack: it must pass on a healthy stack and fail when stock is not reserved or not consumed by shipping.
set -uo pipefail
cd "$(dirname "$0")/../.."
PORT=18080; BASE=http://127.0.0.1:$PORT
run() { # [VAR=value ...] given to the mock -> runs the mock + runner, echoes runner exit code in $RC and output in $OUT
  env "$@" python3 e2e/selftest/mock_stack.py $PORT & local pid=$!
  sleep 1
  OUT=$(KEYCLOAK_URL=$BASE CRM_URL=$BASE ERP_URL=$BASE TIMEOUT=30 bash e2e/run.sh 2>&1); RC=$?
  kill $pid 2>/dev/null; wait $pid 2>/dev/null
}
run
[ $RC -eq 0 ] && grep -q '^E2E_OK quoteId=3 tenant=acme' <<<"$OUT" || { echo "selftest: healthy stack should pass"; echo "$OUT"; exit 1; }
run MOCK_NO_RESERVE=1
[ $RC -ne 0 ] && grep -q 'E2E_FAIL: expected reserved=5' <<<"$OUT" || { echo "selftest: missing reservation must fail"; echo "$OUT"; exit 1; }
run MOCK_NO_CONSUME=1
[ $RC -ne 0 ] && grep -q 'E2E_FAIL: expected onHand=95 after shipping' <<<"$OUT" || { echo "selftest: missing consumption must fail"; echo "$OUT"; exit 1; }
echo "e2e runner selftest: OK (passes when healthy, fails when stock is not reserved or not consumed by shipping)"
