# Customer onboarding contract (CRM side)

Flow: CRM create -> (one DB transaction) customer + mapping `Pending` + outbox row -> OutboxPublisher starts Temporal
workflow `CustomerOnboardingWorkflow` on task queue `orchestration` with workflow id `CustomerOnboarding:<tenant>:<crmCustomerId>[:<attempt>]`
(RejectDuplicate, so redelivery cannot start a second one) -> workflow (Java, not yet written) calls the ERP activity to
validate/create the ERP customer -> calls the CRM activity `crm.record-erp-customer`.

CRM activities (task queue `crm`, all inputs carry tenantId, no tokens):
- `crm.get-customer-for-sync` (CustomerRef) -> CustomerSyncDto
- `crm.record-erp-customer` (tenantId, crmCustomerId, erpCustomerId) - idempotent; a different ERP id for an Active mapping is a conflict
- `crm.mark-sync-failed` (tenantId, crmCustomerId, reason)

Recovery: `POST /api/v1/integration/reconcile` (crm-admin) re-queues this tenant's Failed mappings and Pending ones older than
`olderThanMinutes` (default 15) with attempt+1 (a new workflow id). `GET /api/v1/customers/{id}/integration` shows mapping state.

The Java workflow must: use the contract in `contracts/schemas/customer-sync.v1.json`, retry the ERP activity, and call
`crm.mark-sync-failed` when retries are exhausted. ERP-side inbox/idempotency keyed by (tenantId, crmCustomerId) is also Java work.
Enable on the CRM with `Temporal:Enabled=true` (starts the outbox publisher and the CRM worker in the API process for now).
