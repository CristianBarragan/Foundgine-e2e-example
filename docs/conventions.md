# Engineering conventions (Phase 1)

## API versioning
- URL-prefixed major version: `/api/v1/...`. Breaking change => new `/api/v2`; v1 stays until consumers migrate.
- Additive changes (new optional fields/endpoints) stay in the same version.
- OpenAPI specs live in `contracts/openapi/`, one per service per major version.
- Workflow and event contracts are JSON Schema in `contracts/schemas/` and `contracts/events/`, with a `schemaVersion` field.

## Cross-service identifiers
- Each system owns its own IDs (CRM customer ID != ERP customer ID). Never treated as interchangeable.
- The relationship lives in an integration mapping table (crm_id, erp_id, status).
- Every cross-service call carries `correlationId` (UUID, propagated via `X-Correlation-Id` and Temporal memo) and an idempotency key.
- Workflow IDs are deterministic: `<WorkflowName>:<business-key>` (e.g. `CustomerOnboarding:<crmCustomerId>`).

## Code style
- CRM: .NET 10, nullable enabled, warnings as errors, xUnit. Minimal APIs grouped by version.
- ERP: Java 21, Spring Boot, JUnit 5.
- Secrets never in source; `.env` is git-ignored, `.env.example` documents the variables.
- No access tokens in Temporal payloads/history.
