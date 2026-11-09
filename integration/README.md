# Integration boundary

The CRM and ERP are separate bounded contexts. This directory contains contracts and workflow
specifications, not a shared persistence model.

## Ownership

- CRM adapter: customer, contact, opportunity, commercial order and CRM order status.
- ERP adapter: SKU/product, inventory, warehouse, supplier, reservation and fulfillment.
- Event contracts: immutable integration facts with schema versions and correlation metadata.
- Workflow coordinator: saga state, retries, timeout handling, compensation and reconciliation.

## Contract rules

- Use stable external identifiers; never expose database primary keys as cross-system identity.
- Include `tenantId`, `eventId`, `occurredAt`, `correlationId`, `causationId` and `schemaVersion`.
- Consumers must be idempotent by `eventId` and/or business idempotency key.
- Events describe facts that have occurred; commands express requested work.
- Do not publish sensitive CRM/ERP fields unless explicitly required and authorized.
- Unknown schema versions fail closed or route to a quarantine/dead-letter path.
