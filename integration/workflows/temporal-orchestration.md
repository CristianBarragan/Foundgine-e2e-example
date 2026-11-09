# Temporal orchestration standard

## Decision

Use **Temporal with the .NET SDK in C#** as the enterprise workflow orchestration runtime. Deploy the Temporal worker and authenticated workflow starter API as independent services. CRM remains .NET/C# and ERP remains Java; neither domain embeds or owns the enterprise workflow engine.

## Responsibilities

- **Temporal workflow:** durable process sequencing, waits/timers, retries, timeout policy, compensation ordering, and operator-visible execution state.
- **Activities:** side-effecting calls to CRM/ERP-owned APIs, Foundgine semantic commands, notifications, and other external systems.
- **Foundgine:** common semantic catalog/execution boundary and agent-facing capability contract. It does not become the long-running workflow engine.
- **CRM:** owns customer, contact, opportunity, and commercial-order lifecycle/data.
- **ERP:** owns sales orders, inventory, purchasing, fulfillment and financial records.

## Initial workflow

`CommercialOrderFulfillmentWorkflow` performs:

1. Create the ERP order using a stable idempotency key.
2. Reserve inventory in ERP.
3. Confirm the commercial order in CRM.
4. If reservation is rejected, cancel the ERP order and record rejection in CRM.
5. If a technical failure occurs after reservation, release inventory, cancel the ERP order, and record failure in CRM.

Compensation executes in reverse order. Compensation is itself retried and is visible as part of the workflow history. Operators need a reconciliation path for permanent domain conflicts; never silently swallow failed compensation.

## Trigger and delivery guarantee

The CRM must write a `CommercialOrderAccepted` event to its outbox in the same local transaction that accepts the commercial order. A separate dispatcher starts the Temporal workflow. The dispatcher is at-least-once. Start with the stable workflow ID, use Temporal’s `UseExisting` conflict policy for a running execution, and restrict reuse to failed/canceled/timed-out executions. When a duplicate arrives after a successful or rejected terminal execution, the dispatcher must recognize the already-started response and verify the existing execution rather than creating a second business process. ERP events can signal/update a running workflow when a future process requires asynchronous ERP callbacks.

Temporal does not create a distributed database transaction. The process is a saga with explicit forward and compensating commands.

## Required hardening before production

- Implement and contract-test every CRM/ERP integration endpoint listed in `orchestration/README.md`.
- Add transactional outbox and inbox/deduplication storage to each domain.
- Add tenant-scoped service authorization, Keycloak JWT validation, mTLS/TLS, and least-privilege Temporal access.
- Encrypt workflow payloads, minimize PII in histories, and define retention/visibility policies.
- Add workflow replay tests, activity idempotency tests, failure-injection tests, compensation/reconciliation tests, and end-to-end tests against Temporal test server plus real domain stores.
- Establish workflow versioning and worker deployment strategy before changing workflow code after production executions exist.
