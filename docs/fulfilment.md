# Fulfilment, invoices, payments and returns (CRM side, Phase 8)

Scope: the CRM *shows* what the ERP does after an order exists, and lets sales/support *request returns*. The ERP owns
shipments, invoices, payments and the stock/financial adjustment of a return. Checkpoint 12 adds the durable ERP temporal outbox,
Java `ErpCrmProjectionWorkflow`, and C# customer-to-cash signal handling. The ERP shipment/invoice/payment domain aggregates,
commands, and producer calls are still pending. Not implemented (Java side):
`OrderFulfilmentWorkflow`, `ShipmentTrackingWorkflow`, `InvoiceGenerationWorkflow`, `PaymentReconciliationWorkflow`,
`InventoryReplenishmentWorkflow` and the ERP shipment/invoice/payment activities. `ReturnProcessingWorkflow` and its ERP
activities now exist (see below). Procurement and inventory ownership are ERP-only.

## ERP -> CRM (Temporal activities on the `crm` queue)
`crm.record-shipment`, `crm.record-invoice`, `crm.record-payment` (schemas: `contracts/schemas/fulfilment-activities.v1.json`).
- Keyed by the ERP id (`tenant_id + erp_*_id`), resolved to a CRM quote through `quotes.erp_order_id`.
- Upsert applies only if the incoming `version` is **greater** than the stored one: duplicates are no-ops, stale/out-of-order
  deliveries are ignored, so status never goes backwards. The ERP must assign a per-entity increasing `version`.
- Unknown ERP order -> `CrmNotFoundException`; the workflow's retry policy covers the case where the order is recorded a moment later.
- Invoices are linked to the CRM customer through the quote; payments carry the ERP invoice id.

## Returns (CRM-owned request, ERP decision)
`POST /quotes/{id}/returns {reason, lines:[{sku,quantity}]}` (crm-sales/crm-admin/enterprise-admin) on an `Ordered` quote:
- SKUs must be on the quote; per SKU, quantity across all non-rejected returns cannot exceed the ordered quantity
  (checked under a row lock on the quote, so concurrent requests cannot oversubscribe).
- In one transaction: `return_requests` + lines + a `ReturnRequested` outbox message. The publisher starts
  `ReturnProcessingWorkflow` (id `ReturnProcessing:<tenant>:<returnId>`, RejectDuplicate; `contracts/schemas/return-requested.v1.json`).
- The workflow reports back with `crm.record-return-decision`: Requested -> Authorized (needs `erpReturnId`) | Rejected; Authorized -> Completed.
  The same decision again is a no-op; a different ERP return id or an illegal transition is a conflict.

## Reads (any CRM role)
`GET /quotes/{id}/fulfilment` (shipments, invoices, payments), `GET /customers/{id}/invoices`,
`GET /quotes/{id}/returns`, `GET /returns/{id}`. All tenant-scoped; another tenant's ids return 404.

## Reconciliation of failed integrations
Fulfilment facts are re-pushed by the ERP workflows (idempotent), so reconciliation is ERP-driven. A return whose workflow never
started is retried by the outbox publisher; a return stuck in `Requested` can be found with
`SELECT ... FROM return_requests WHERE status = 'Requested' AND updated_at < now() - interval '1 hour'` (the expired-wait case has the `received-late` operator path below; a general admin reconcile
endpoint for returns is not written yet).

## Return processing, Java side
`ReturnProcessingWorkflow` (orchestration queue; id `ReturnProcessing:<tenant>:<returnId>`):
1. `erp.authorize-return`: Authorized or Rejected (business refusals are decisions, not failures); idempotent on tenant + CRM return id.
2. `crm.record-return-decision` Authorized (with the ERP return id) or Rejected (with the note).
3. Authorized: waits up to 30 days for the `goodsReceived` signal, then `erp.complete-return` (stock back on hand, once) and
   `crm.record-return-decision` Completed. No signal in time: the return stays Authorized (result `AwaitingGoodsExpired`).
Schemas: `contracts/schemas/return-activities.v1.json`. Decisions items 21 and 26.

### Confirming receipt (the authorized signal sender)
`POST /api/v1/returns/{returnId}/goods-received` on the ERP (`{returnId}` is the CRM return id), no body.
- Roles: `erp-inventory`, `erp-admin`, `enterprise-admin`. Anyone else: 403. No token: 401. A token without `tenant_id` (service account): 403.
- Tenant and sender come only from the token: the workflow id is built as `ReturnProcessing:<token tenant>:<returnId>` and the
  signal carries `receivedBy = token subject`. Nothing in the body or query string is read, so one tenant cannot signal another's workflow.
- The ERP return is looked up first: `Authorized` -> signal, 202 `{"status":"signalled"}`; `Completed` -> 200 `{"status":"alreadyCompleted"}`;
  unknown or another tenant's return -> 404; `Rejected` -> 409; workflow no longer running (30-day wait expired) -> 409 (an administrator then uses `received-late`, below);
  `temporal.enabled=false` -> 503. Repeating the call is safe (the workflow remembers the signal).
- The workflow ignores a signal with no `receivedBy`.
- Temporal itself does not authenticate signal senders: anyone who can reach the Temporal frontend can still signal directly, e.g.
  `temporal workflow signal --namespace enterprise --workflow-id ReturnProcessing:<tenant>:<returnId> --name goodsReceived --input '{"receivedBy":"operator-name"}'`.
  Treat that as break-glass and keep the frontend off any network the API users reach.

### Operator path: goods arrived after the 30-day wait expired
`POST /api/v1/returns/{returnId}/received-late` on the ERP (`{returnId}` is the CRM return id), no body. Roles: `erp-admin`,
`enterprise-admin` only (warehouse staff use `goods-received` while the workflow still waits). No token: 401. No `tenant_id`: 403.
- The ERP return is looked up by the token's tenant; the ERP return id comes from the ERP's own row, never from the request.
  Unknown or another tenant's return -> 404; `Rejected` -> 409; `Completed` -> 200 `{"status":"alreadyCompleted"}`.
- If the original `ReturnProcessing` workflow is still running -> 409 (use `goods-received`). If it is closed or its history is gone,
  the ERP starts `ReturnResolutionWorkflow` (id `ReturnResolution:<tenant>:<returnId>`, orchestration queue): 202 `{"status":"resolving"}`.
  A second call while it runs or after it completed -> 200 `{"status":"alreadyInProgress"}`; a failed run may be started again.
- `ReturnResolutionWorkflow`: `erp.complete-return` (stock back once; a Completed return changes nothing) then `crm.record-return-decision`
  Completed, with a note naming the operator (`resolvedBy` = token subject). Same retry rules as `ReturnProcessingWorkflow`.
- `temporal.enabled=false` -> 503 (only when Temporal would be needed). The ERP reads `temporal.orchestration-task-queue` (default `orchestration`).

## ERP-owned shipment, invoice and payment commands (Checkpoint 13)

The ERP exposes authenticated commands under `/api/v1`:

- `POST /orders/{orderId}/shipments` (`erp-inventory`): creates a `Packed` shipment; supply a unique `idempotencyKey` in the body.
- `POST /shipments/{shipmentId}/transitions` (`erp-inventory`): advances `Packed -> Shipped -> InTransit -> Delivered`; cancellation is allowed before delivery.
- `POST /orders/{orderId}/invoices` (`erp-finance`): issues one invoice for the ERP order total, with a default 30-day due date.
- `POST /invoices/{invoiceId}/payments` (`erp-finance`): records a payment as `Received`; supply a unique `idempotencyKey` in the body.
- `POST /payments/{paymentId}/reconcile` (`erp-finance`): reconciles a received payment and updates invoice balance/status.
- `POST /payments/{paymentId}/reverse` (`erp-finance`): reverses a received or reconciled payment and recomputes the invoice status.

All tenant scope is read from the validated JWT `tenant_id` claim. Every write and corresponding ERP Temporal outbox event execute in the same database transaction. The relay delivers versioned facts to the `CustomerToCash:{tenant}:{quoteId}` C# workflow; delivery is at-least-once and entity-version guarded.
