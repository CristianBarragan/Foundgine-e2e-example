# Fulfilment, invoices, payments and returns (CRM side, Phase 8)

Scope: the CRM *shows* what the ERP does after an order exists, and lets sales/support *request returns*. The ERP owns
shipments, invoices, payments and the stock/financial adjustment of a return. Not implemented (Java side):
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
`SELECT ... FROM return_requests WHERE status = 'Requested' AND updated_at < now() - interval '1 hour'` (an admin reconcile
endpoint for returns is not written yet).

## Return processing, Java side
`ReturnProcessingWorkflow` (orchestration queue; id `ReturnProcessing:<tenant>:<returnId>`):
1. `erp.authorize-return`: Authorized or Rejected (business refusals are decisions, not failures); idempotent on tenant + CRM return id.
2. `crm.record-return-decision` Authorized (with the ERP return id) or Rejected (with the note).
3. Authorized: waits up to 30 days for the `goodsReceived` signal, then `erp.complete-return` (stock back on hand, once) and
   `crm.record-return-decision` Completed. No signal in time: the return stays Authorized (result `AwaitingGoodsExpired`).
Send the signal with the Temporal CLI, e.g.
    temporal workflow signal --namespace enterprise --workflow-id ReturnProcessing:<tenant>:<returnId> --name goodsReceived --input '{"receivedBy":"warehouse-1"}'
Schemas: `contracts/schemas/return-activities.v1.json`. Decisions item 21.

