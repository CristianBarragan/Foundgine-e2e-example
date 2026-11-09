# Foundgine orchestration runtime

Temporal is the durable workflow engine for cross-domain business processes. The orchestrator is implemented in C#/.NET and runs independently from the CRM API and Java ERP.

## Projects

- `Foundgine.Orchestration.Process`: the `commercial-order-fulfillment` process definition (steps + compensations) and serializable DTOs. The workflow itself is Foundgine's generic `FoundgineProcessWorkflow` (Foundgine.Extensions.Temporal 2.3.1).
- `Foundgine.Orchestration.Worker`: `AddFoundgineTemporalWorker` host; one `IFoundgineTemporalOperation` per step/compensation, each an HTTP anti-corruption adapter for CRM/ERP integration endpoints. Keycloak warrants via `AddFoundgineKeycloakWarrants(...).UseForTemporalWorker()` (config section `Foundgine:Keycloak`).
- `Foundgine.Orchestration.Api`: authenticated workflow-start endpoint. It validates the Keycloak JWT and checks that the `tenant_id` claim matches the request tenant.

## Local development

Install the .NET 10 SDK and Temporal CLI, then start the Temporal development service:

```bash
temporal server start-dev
```

The Temporal service listens on `localhost:7233`; its UI is normally at `http://localhost:8233`.

Run the worker (configure the CRM and ERP adapter base URLs first):

```bash
TEMPORAL_ADDRESS=localhost:7233 \
ERP_BASE_URL=http://localhost:8082 \
CRM_BASE_URL=http://localhost:8081 \
dotnet run --project orchestration/Foundgine.Orchestration.Worker
```

Run the starter API with Keycloak configuration:

```bash
KEYCLOAK_AUTHORITY=https://identity.example/realms/foundgine \
KEYCLOAK_AUDIENCE=foundgine-orchestration \
TEMPORAL_ADDRESS=localhost:7233 \
dotnet run --project orchestration/Foundgine.Orchestration.Api
```

Workflow ids are `foundgine/{tenant}/commercial-order-fulfillment/{orderId:N}`; the starter (`IFoundgineWorkflowStarter`) absorbs repeats, so API, outbox redelivery and retries address the same workflow.

The API accepts an authenticated request at:

`POST /internal/workflows/commercial-orders/{orderId}/fulfillment`

The body is `CommercialOrderWorkflowInput`. The token must contain a `tenant_id` claim matching `TenantId`; configure the Keycloak protocol mapper to emit this claim, or set `TENANT_CLAIM_TYPE` to the agreed claim name.

## Integration endpoints still required

The worker deliberately calls domain-owned adapter routes rather than writing to either database:

| Domain | Method and route | Purpose |
|---|---|---|
| ERP | `POST /api/integration/orders` | Create ERP order; return `ErpOrderResult` |
| ERP | `POST /api/integration/inventory/reservations` | Reserve inventory; return `InventoryReservationResult` |
| ERP | `POST /api/integration/inventory/reservations/{reservationId}/release` | Compensate a reservation |
| ERP | `POST /api/integration/orders/{erpOrderId}/cancel` | Cancel the ERP order |
| CRM | `POST /api/integration/commercial-orders/{crmOrderId}/fulfillment-confirmed` | Mark fulfillment orchestration successful |
| CRM | `POST /api/integration/commercial-orders/{crmOrderId}/fulfillment-failed` | Record a failed/rejected orchestration state |

These routes are **contracts to implement**, not routes claimed to exist in the supplied CRM/ERP code. Each domain must authorize the tenant, validate its own invariants, and persist its own state. All commands carry stable idempotency keys; the receiving domain must enforce uniqueness on those keys.

## Operational rules

1. Start workflows from a transactional outbox dispatcher after the CRM commercial-order commit; do not start them before the order is durable.
2. The outbox dispatcher must retry safely and use the stable workflow ID `commercial-order-fulfillment:{tenantId}:{crmOrderId}`.
3. Temporal activities call domain APIs only. No workflow or activity may connect directly to CRM or ERP databases.
4. Activities must be idempotent. Temporal retries can repeat an Activity after an ambiguous network failure.
5. Compensation is a domain command, not a database rollback. Failed compensation remains visible in Temporal and must page/queue an operator for reconciliation.
6. Run Temporal with TLS, authenticated workers, isolated namespaces/task queues, restricted network access, and encrypted payloads in production. Do not put access tokens or secrets in workflow inputs/history.
7. Add payload codecs/encryption, workflow versioning, worker deployment versioning, visibility/search attributes, metrics/tracing, and retention policies before production rollout.

Temporal owns process state and execution history; CRM and ERP remain authoritative for their business entities and ledgers. Foundgine remains the shared semantic execution boundary used by agents and domain applications. Temporal is the coordinator, not a replacement for Foundgine or either domain model.


## Transactional outbox dispatcher

`Foundgine.Crm.OutboxDispatcher` is a CRM-owned process. Apply the outbox SQL migration,
emit `CommercialOrderAccepted` in the same transaction as CRM order acceptance, then run
the dispatcher. It runs Foundgine's own outbox dispatcher (`foundgine_outbox`, Providers `PostgresOutbox`) with the
Temporal publisher bridge, which leases rows, starts the process and handles retry/dead-letter. See its README for configuration. The CRM commercial-order aggregate and
producer now exist (`POST /api/commercial-orders`, see `applications/crm-dotnet/backend/CommercialOrderService.cs`),
and the two CRM callback routes above are implemented. The four ERP routes are implemented in
`applications/erp-java/.../integration/` (`IntegrationServer`, run with its `main`). Nothing has been run end to end yet.

Apply order in the CRM database: EF `AddInitial` -> `Outbox.sql` -> `CommercialOrders.sql`. Grant the worker's
service account the realm role `orchestration-service` and an audience mapper for `crm-api`.


### ERP integration contract (as implemented)

- Auth: `Authorization: Bearer` Keycloak token, RS256, issuer/audience checked, realm role `orchestration-service`
  (env `ERP_JWT_ISSUER`, `ERP_JWT_AUDIENCE`, `ERP_JWKS_URI`). Other roles get 403, bad tokens 401.
- Tenant confinement is by data: the command's `tenantId` filters customers, warehouses/lots, orders and reservations.
- `productId` in a line is the ERP **SKU**. The CRM customer must be mapped first:
  `INSERT INTO customer_external_refs(tenant_id,source_system,external_id,customer_id) VALUES ('tenant-a','crm','<crm company uuid>',1)`.
  Unmapped customer or unknown SKU -> 422; fractional quantity -> 422 (ERP quantities are whole units).
- Reservation uses `inventory_lots.reserved` (FIFO across lots, all-or-nothing). Insufficient stock is a normal `200`
  with `isReserved:false` so the workflow takes its rejection path. Cancelling an order that still holds a reservation is 409.
- Every command is idempotent by `(tenantId, idempotencyKey)` with a request fingerprint; replay returns the stored response.
- The worker now treats 4xx (except 408/429) as non-retryable activity failures.


## Saga shape on the generic process (rejection handling)

Steps: `begin` -> `create-erp-order` -> `reserve-inventory` -> `assert-reserved` -> `confirm-crm`. Compensations run in
reverse: `release-inventory`, `cancel-erp-order`, then `mark-crm-failed` (registered on `begin`, so it runs last).
Insufficient stock is returned by `reserve-inventory` as output (`isReserved:false`); `assert-reserved` fails
non-retryably with `REJECTED: <reason>`. CRM receives `fulfillment-failed` with that `REJECTED:` reason (business
rejection) versus `Workflow failure` (technical). **The Temporal result for a rejection is the generic process `Failed`
state, not a custom `Rejected` result** - the distinction lives in the CRM record. Replace with a custom workflow
only if the Temporal-level `Rejected` state is required.
