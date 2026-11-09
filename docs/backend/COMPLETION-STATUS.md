# Backend completion status — Temporal outbox tranche

## Implemented
- CRM PostgreSQL transactional-outbox schema with leases, retry metadata and delivery timestamps.
- Separate CRM-owned .NET dispatcher using `FOR UPDATE SKIP LOCKED`.
- Deterministic Temporal workflow IDs and `UseExisting` to tolerate crash after workflow start.
- Payload tenant/aggregate validation and persisted retry errors with exponential backoff.

- CRM CommercialOrder aggregate (`CommercialOrders.sql`, `CommercialOrderService`): order, lines and `CommercialOrderAccepted` outbox row insert in ONE Npgsql transaction; `POST /api/commercial-orders` is idempotent by `Idempotency-Key` + request fingerprint.
- CRM orchestration callbacks `fulfillment-confirmed` / `fulfillment-failed` (service role `orchestration-service`), de-duplicated by `crm_integration_inbox`, order row locked `FOR UPDATE`.
- Dispatcher treats `WorkflowAlreadyStartedException` as delivered (previously a completed workflow + crash-before-ack would retry forever).
- Unit tests for validation, fingerprinting, outbox payload shape and callback validation (`CommercialOrderValidationTests`) - written, not executed.

- ERP integration endpoints (Java): `integration.sql`, `IntegrationStore`, `IntegrationServer`, `JwtVerifier` (RS256/JWKS, issuer, audience, role), `ReservationPlanner`. Type-checked here with Jackson stubs; `ReservationPlannerTest` run (5 pass). JDBC logic, JWT verification and HTTP wiring are NOT executed - no PostgreSQL, Jackson or Maven available.
- Worker: 4xx integration responses (except 408/429) are non-retryable (uses `ApplicationFailureException(nonRetryable: true)`; unverified against the SDK).

## Still incomplete
- ERP: connection-per-request (no pool), no metrics, `unitOfMeasure`/`currency` ignored, reservation does not yet convert to shipment/allocation. Existing MCP server still has no JWT validation (only the new integration server does).
- ERP customer mapping is a manual table (`customer_external_refs`) with no admin API.
- Commercial orders are not yet in the Foundgine semantic model (read via explicit tenant-scoped SQL for now); no AI/MCP visibility.
- Keycloak confidential client + service account for the worker (role `orchestration-service` exists in the realm; client, secret and audience mapper for `crm-api` do not). Worker currently sends one shared `SERVICE_TO_SERVICE_TOKEN` to both CRM and ERP.
- Saga hazard (reduced): a 409 on a late `fulfillment-failed` now fails the compensation activity non-retryably instead of looping, but nothing pages an operator yet.
- Event schema drift: `commercial-order-accepted.v1.schema.json` describes `sku` lines and an envelope; the outbox payload is the orchestrator's `CommercialOrderWorkflowInput` (`productId`, `customerId`). Reconcile before any second consumer exists.
- ERP integration HTTP endpoints and persistent idempotency.
- ERP Keycloak JWT/JWKS validation and tenant-bound request context.
- Temporal TLS/mTLS, payload encryption, DLQ/replay, metrics and alerts.
- Build, integration and failure-injection tests. .NET SDK was unavailable; compilation/live execution are unverified.

## Acceptance checks
- [ ] Outbox insert is atomic with order acceptance. (implemented; needs integration test against PostgreSQL)
- [ ] Multiple dispatchers cannot claim the same lease concurrently.
- [ ] Crash after Temporal start does not create a duplicate workflow.
- [ ] Temporal outage retries and later dispatches.
- [ ] Invalid payloads and tenant mismatch are surfaced for operator action.
- [ ] End-to-end test with PostgreSQL, Temporal, CRM, ERP and Keycloak.
