# Customer order and inventory reservation saga

**Status: proposed workflow; not implemented in the supplied source archives.**

## Happy path

1. CRM creates/accepts the commercial order and emits `CommercialOrderAccepted`.
2. The workflow coordinator validates the event and calls the ERP adapter to request reservation.
3. ERP validates customer/warehouse/stock policy and performs an idempotent reservation.
4. ERP emits `InventoryReservationConfirmed` or `InventoryReservationRejected`.
5. CRM consumes the outcome and updates its own order projection/status.
6. Foundgine exposes the combined state as a composed semantic view, with each field attributed
   to its authoritative owner.

## Failure and compensation

- If reservation fails, CRM marks the order as requiring attention or rejected according to
  business policy; it does not fabricate an inventory reservation.
- If CRM cannot consume a confirmed reservation event, the event is retried and eventually
  reconciled from ERP by reservation reference.
- If the commercial order is cancelled after reservation, the coordinator requests ERP
  reservation release. ERP validates and executes the release idempotently.
- If the outcome is unknown after timeout, query by idempotency key before retrying a mutation.

## Invariants

- No distributed ACID transaction.
- ERP is authoritative for inventory and reservation.
- CRM is authoritative for the commercial order.
- All commands carry an idempotency key and correlation identifiers.
- Every transition is auditable and tenant-scoped.
