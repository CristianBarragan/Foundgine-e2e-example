# Opportunities, quotes and quote-to-order (CRM side, Phase 7)

Opportunity stages: Prospecting -> Qualification -> Proposal -> Negotiation (one step forward at a time), ClosedLost from any open
stage. ClosedWon is only set by accepting a quote.

Quote: Draft -> submit -> PendingApproval (total > `Quotes:ApprovalThreshold`, default 10000) or Approved (auto) -> approve/reject by a
crm-admin/enterprise-admin who is NOT the submitter -> accept -> Accepted -> (ERP order recorded) Ordered.
All transitions are conditional UPDATEs on the expected status, so duplicates/races apply once.

Accept requires an Active customer integration mapping (Phase 6). In one transaction it marks the quote Accepted, closes the
opportunity as ClosedWon and writes a `QuoteAccepted` outbox message. The publisher starts `QuoteToOrderWorkflow`
(id `QuoteToOrder:<tenant>:<quoteId>`, RejectDuplicate) with `contracts/schemas/quote-accepted.v1.json`.

The Java workflow is expected to: keep the ERP order + stock reservation as one synchronous atomic ERP transaction, key it by
(tenantId, quoteId), then call `crm.record-erp-order` (tenantId, quoteId, erpOrderId; idempotent, conflict on a different id).
Not implemented here: the Java QuoteApprovalWorkflow (approval is a CRM state machine for now), OrderCancellationWorkflow/compensation,
ERP pricing/tax/inventory.

Endpoints: POST /customers/{id}/opportunities, GET /customers/{id}/opportunities, GET /customers/{id}/sales-history,
GET|PUT /opportunities/{id}, POST /opportunities/{id}/quotes, GET /quotes/{id}, PUT /quotes/{id}/lines,
POST /quotes/{id}/submit|approve|reject|accept.

## Java side (ERP + orchestration)
`QuoteToOrderWorkflow` (orchestration queue) runs: `erp.create-order-with-reservation` -> `crm.record-erp-order`. The ERP step is one
atomic transaction keyed by (tenantId, quoteId): replays return the same order and reserve nothing twice. Failures:
- validation / pricing floor / unknown SKU / inactive customer -> `ValidationFailed`, stock short -> `InsufficientStock`: not retried,
  nothing is left behind, workflow result `Failed`; the quote moves `Accepted` -> `OrderFailed` via `crm.mark-order-failed`
  (reason stored in `quotes.order_failure_reason`; decisions item 20).
- permanent CRM refusal (`CrmNotFoundException`, `CrmValidationException`, `CrmConflictException`) -> `erp.cancel-order` releases the
  reservation, then the quote is marked `OrderFailed` too (if the CRM allows), result `Failed`. A CRM outage is retried without limit instead.
Seed data until ERP catalogue endpoints exist (per tenant):
    INSERT INTO erp_products (tenant_id, sku, name, list_price, min_unit_price) VALUES ('acme','SKU-1','Widget',100,80);
    INSERT INTO erp_inventory (tenant_id, sku, on_hand) VALUES ('acme','SKU-1',50);

