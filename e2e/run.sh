#!/usr/bin/env bash
# End-to-end proof of the CRM quote -> ERP order -> reservation -> shipment -> invoice -> payment -> CRM journey.
# Runs inside the compose network (see infrastructure/docker-compose.demo.yml). Real Keycloak tokens, no dev headers.
# Last line of stdout on success: E2E_OK quoteId=<n> tenant=acme
set -euo pipefail

KEYCLOAK_URL=${KEYCLOAK_URL:-http://keycloak:8080}
CRM_URL=${CRM_URL:-http://crm-api:5000}
ERP_URL=${ERP_URL:-http://erp:8082}
SKU=${SKU:-SKU-1}
QTY=${QTY:-5}
UNIT_PRICE=${UNIT_PRICE:-100}
TIMEOUT=${TIMEOUT:-180}

say()  { printf '\n== %s\n' "$*"; }
fail() { printf '\nE2E_FAIL: %s\n' "$*" >&2; exit 1; }

# call METHOD URL TOKEN [JSON] -> prints the body; fails on a non-2xx response.
call() {
  local method=$1 url=$2 token=$3 body=${4:-} out code
  out=$(mktemp)
  if [ -n "$body" ]; then
    code=$(curl -s -o "$out" -w '%{http_code}' -X "$method" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' -d "$body" "$url")
  else
    code=$(curl -s -o "$out" -w '%{http_code}' -X "$method" -H "Authorization: Bearer $token" "$url")
  fi
  if [[ ! $code =~ ^2 ]]; then echo "HTTP $code from $method $url: $(cat "$out")" >&2; rm -f "$out"; return 1; fi
  cat "$out"; rm -f "$out"
}

token() { # user
  curl -sf -d grant_type=password -d client_id=enterprise-web -d "username=$1" -d password=dev-password \
    "$KEYCLOAK_URL/realms/enterprise/protocol/openid-connect/token" | jq -er .access_token
}

# until DESCRIPTION COMMAND...: retry COMMAND (stdout hidden) every 2 s until it succeeds or TIMEOUT passes.
until_ok() {
  local what=$1; shift; local end=$((SECONDS + TIMEOUT))
  until "$@" >/dev/null 2>&1; do
    [ $SECONDS -lt $end ] || fail "timed out waiting for: $what"
    sleep 2
  done
}

say "Tokens (alice = crm-sales/acme, dave = enterprise-admin/acme)"
ALICE=$(token alice); DAVE=$(token dave)

say "Stock before"
stock() { call GET "$ERP_URL/api/v1/inventory/$SKU" "$DAVE"; }
BEFORE=$(stock); echo "$BEFORE"
AVAIL0=$(jq -r .available <<<"$BEFORE"); RES0=$(jq -r .reserved <<<"$BEFORE"); ONHAND0=$(jq -r .onHand <<<"$BEFORE")
[ "$AVAIL0" -ge "$QTY" ] || fail "not enough demo stock for $SKU (available=$AVAIL0, need $QTY); re-run the seed"

say "CRM: customer, opportunity, quote"
CUSTOMER=$(call POST "$CRM_URL/api/v1/customers" "$ALICE" \
  "{\"kind\":\"Business\",\"name\":\"E2E Ltd $RANDOM\",\"email\":\"e2e-$RANDOM$RANDOM@example.test\",\"phone\":\"021 555 0100\"}")
CID=$(jq -er .id <<<"$CUSTOMER")
OPP=$(call POST "$CRM_URL/api/v1/customers/$CID/opportunities" "$ALICE" '{"name":"E2E deal","amount":100}')
OID=$(jq -er .id <<<"$OPP")
QUOTE=$(call POST "$CRM_URL/api/v1/opportunities/$OID/quotes" "$ALICE" \
  "{\"lines\":[{\"sku\":\"$SKU\",\"description\":\"Widget\",\"quantity\":$QTY,\"unitPrice\":$UNIT_PRICE}]}")
QID=$(jq -er .quote.id <<<"$QUOTE")
echo "customer=$CID opportunity=$OID quote=$QID"

STATUS=$(call POST "$CRM_URL/api/v1/quotes/$QID/submit" "$ALICE" | jq -er .quote.status)
if [ "$STATUS" = PendingApproval ]; then
  call POST "$CRM_URL/api/v1/quotes/$QID/approve" "$DAVE" >/dev/null
fi

say "CRM: accept (retries until the ERP customer sync has made the mapping Active)"
accept() { call POST "$CRM_URL/api/v1/quotes/$QID/accept" "$ALICE"; }
until_ok "customer synchronised to the ERP so the quote can be accepted" accept

say "Wait for quote Ordered (CustomerToCash -> Java QuoteToOrder -> ERP order + reservation -> CRM link)"
quote_ordered() { [ "$(call GET "$CRM_URL/api/v1/quotes/$QID" "$ALICE" | jq -r .quote.status)" = Ordered ]; }
until_ok "quote $QID to become Ordered" quote_ordered
ORDER=$(call GET "$CRM_URL/api/v1/quotes/$QID" "$ALICE" | jq -er .quote.erpOrderId)
echo "erpOrderId=$ORDER"

say "ERP stock reserved"
AFTER=$(stock); echo "$AFTER"
[ "$(jq -r .reserved <<<"$AFTER")" -eq $((RES0 + QTY)) ] || fail "expected reserved=$((RES0 + QTY)), got $(jq -r .reserved <<<"$AFTER")"
[ "$(jq -r .available <<<"$AFTER")" -eq $((AVAIL0 - QTY)) ] || fail "expected available=$((AVAIL0 - QTY)), got $(jq -r .available <<<"$AFTER")"

say "ERP: ship, deliver, invoice, pay, reconcile"
SHIP=$(call POST "$ERP_URL/api/v1/orders/$ORDER/shipments" "$DAVE" \
  "{\"carrier\":\"NZ Post\",\"trackingNumber\":\"TRK-$QID\",\"idempotencyKey\":\"e2e-ship-$QID\"}")
SID=$(jq -er .erpShipmentId <<<"$SHIP")
for next in Shipped InTransit Delivered; do
  call POST "$ERP_URL/api/v1/shipments/$SID/transitions" "$DAVE" "{\"status\":\"$next\"}" >/dev/null
done
INV=$(call POST "$ERP_URL/api/v1/orders/$ORDER/invoices" "$DAVE" '{}')
IID=$(jq -er .erpInvoiceId <<<"$INV"); TOTAL=$(jq -er .total <<<"$INV")
PAY=$(call POST "$ERP_URL/api/v1/invoices/$IID/payments" "$DAVE" "{\"amount\":$TOTAL,\"idempotencyKey\":\"e2e-pay-$QID\"}")
PID=$(jq -er .erpPaymentId <<<"$PAY")
call POST "$ERP_URL/api/v1/payments/$PID/reconcile" "$DAVE" >/dev/null
echo "shipment=$SID invoice=$IID total=$TOTAL payment=$PID"

say "CRM projection converges (ERP outbox -> Temporal -> ErpCrmProjectionWorkflow -> CRM)"
projected() {
  local f; f=$(call GET "$CRM_URL/api/v1/quotes/$QID/fulfilment" "$ALICE") || return 1
  jq -e '(.shipments|length==1 and .[0].status=="Delivered") and (.invoices|length==1 and .[0].status=="Paid") and (.payments|length==1 and .[0].status=="Reconciled")' <<<"$f"
}
until_ok "CRM to show Delivered / Paid / Reconciled" projected
call GET "$CRM_URL/api/v1/quotes/$QID/fulfilment" "$ALICE" | jq '{shipments:[.shipments[]|{erpShipmentId,status,version}],invoices:[.invoices[]|{erpInvoiceId,status,version}],payments:[.payments[]|{erpPaymentId,status,version}]}'

say "ERP stock consumed by shipping"
FINAL=$(stock); echo "$FINAL"
[ "$(jq -r .onHand <<<"$FINAL")" -eq $((ONHAND0 - QTY)) ] || fail "expected onHand=$((ONHAND0 - QTY)) after shipping, got $(jq -r .onHand <<<"$FINAL")"
[ "$(jq -r .reserved <<<"$FINAL")" -eq "$RES0" ] || fail "expected reserved=$RES0 after shipping, got $(jq -r .reserved <<<"$FINAL")"

printf '\nE2E_OK quoteId=%s tenant=acme\n' "$QID"
