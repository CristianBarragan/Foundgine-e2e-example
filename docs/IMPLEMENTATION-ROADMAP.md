# Implementation roadmap

## Increment 1 — Contract and packaging baseline (delivered)
- Preserve the original CRM and ERP project trees.
- Establish repository ownership boundaries and architecture documentation.
- Add versioned event schemas and a saga specification.
- Record known build, identity, runtime-version and eventing gaps.

## Increment 2 — Reproducible independent builds (in progress)
- [x] Remove the Java sample's dependency on the missing external Maven parent; pin its direct dependency graph and Java 21 target.
- [ ] Pin the .NET SDK and add a repository CI matrix.
- [ ] Run CRM unit tests and ERP JUnit tests in CI.
- [ ] Add production container entrypoints and health checks for both services.
- [ ] Verify the exact Foundgine .NET 2.3.1 / Java 2.3.1 semantic-contract compatibility.


## Increment 3 — Identity and adapter contracts
- Agree common Keycloak issuer/audience/tenant/role semantics.
- Implement ERP JWT validation or a trusted identity exchange.
- Implement CRM and ERP adapters with contract tests.
- Keep owner-side authorization mandatory.

## Increment 4 — Events and saga
- Select and deploy the enterprise event bus.
- Implement transactional outbox/inbox and idempotent consumers.
- Implement reservation request, confirmation, rejection and release.
- Add retries, dead-letter handling, timeout reconciliation and compensation.

## Increment 5 — Shared Foundgine runtime boundary
- Decide whether the enterprise semantic boundary is a standalone gateway, a set of shared
  libraries, or both. The proposed target architecture calls for a separately deployable
  platform; this runtime is not included in the source archives.
- Implement semantic capability aggregation without exposing raw database schemas.
- Add policy evaluation, dry-run, approvals, audit and observability.
- Expose governed MCP/REST/GraphQL interfaces and contract compatibility tests.

## Increment 6 — End-to-end assurance
- Tenant-isolation and cross-tenant reference tests.
- Authorization denial and privilege-escalation tests.
- Mutation idempotency, replay, race and compensation tests.
- Failure injection for event delivery and service outages.
- Full workflow E2E from CRM commercial order through ERP reservation and CRM status update.

See `docs/backend/COMPLETION-STATUS.md` for the current implementation ledger and release gates.


## Temporal orchestration workstream

1. [x] Add C# Temporal workflow, worker, authenticated starter API, and initial commercial-order saga scaffold.
2. [x] Implement CRM commercial-order state and transactional outbox event/dispatcher (unbuilt/untested - no .NET SDK in the authoring environment).
3. [x] Implement ERP idempotent order, inventory reservation, release, and cancellation integration endpoints (type-checked only; needs PostgreSQL integration tests).
4. [x] Implement CRM orchestration callback endpoints (service role + idempotency inbox; tenant taken from command body and verified against the order row).
5. [ ] Add Temporal payload encryption, worker authentication, workflow versioning, metrics/tracing, and production persistence/retention configuration.
6. [ ] Add Temporal workflow replay, activity retry/idempotency, compensation, and end-to-end tests.
