# Enterprise platform: architecture documentation

CRM (.NET 10 + Foundgine) and ERP (Java + Spring Boot) integrated through Temporal workflows, with Keycloak for identity.
All diagrams are PlantUML. Sources are in [`diagrams/src/`](diagrams/src); rendered SVGs are in [`diagrams/`](diagrams).
Regenerate with `./render.sh` (Java + Graphviz + PlantUML). The step-by-step guide is in [`tutorial.md`](tutorial.md).

> **Read this first: verification status.** This document describes the code as written. Per `docs/decisions.md`
> (items 9, 12, 15-21), the C# has never been compiled, the Spring/JdbcClient code and the Temporal workflow tests have never
> been run, and several SDK calls are written from memory. Pure rules and most ERP SQL were run in isolation. Treat every
> "how it behaves" statement below as the *intended and coded* behaviour until a real build and test run confirms it.

## Contents
1. [Scope and goals](#1-scope-and-goals)
2. [Architectural principles](#2-architectural-principles)
3. [System context](#3-system-context)
4. [Containers and deployment](#4-containers-and-deployment)
5. [CRM internals](#5-crm-internals)
6. [ERP and orchestration internals](#6-erp-and-orchestration-internals)
7. [Integration contracts](#7-integration-contracts)
8. [Key flows](#8-key-flows)
9. [State machines](#9-state-machines)
10. [Data architecture](#10-data-architecture)
11. [Security architecture](#11-security-architecture)
12. [Reliability, idempotency and failure modes](#12-reliability-idempotency-and-failure-modes)
13. [Observability and operations](#13-observability-and-operations)
14. [Implementation status and open items](#14-implementation-status-and-open-items)
15. [Glossary](#15-glossary)

---

## 1. Scope and goals

The platform lets a sales organisation manage customers and quotes in a CRM while an ERP owns orders, stock and
financial outcomes. The two systems are developed, deployed and failed independently; Temporal provides durable,
cross-language orchestration between them.

Business capabilities implemented so far:

| Capability | CRM | ERP | Workflow |
|---|---|---|---|
| Customer master and contacts | owns | holds its own customer record | `CustomerOnboardingWorkflow` |
| Opportunities and quotes (with segregated approval) | owns | none | none (CRM state machine) |
| Quote acceptance to sales order, with stock reservation | records result | owns order + reservation | `QuoteToOrderWorkflow` |
| Returns | owns the request | authorises, restocks | `ReturnProcessingWorkflow` |
| Shipments, invoices, payments | read-only mirrors | owns (producers not written) | planned |

## 2. Architectural principles

| # | Principle | Where it shows up |
|---|---|---|
| P1 | **Each system owns its data and identifiers.** IDs are never interchangeable; relationships live in explicit mapping rows. | [Identifier ownership](#10-data-architecture), `customer_integration_mappings`, `quotes.erp_order_id` |
| P2 | **No distributed transactions.** Each side commits locally; Temporal coordinates with retries and compensation. | Outbox in CRM, atomic order+reservation in ERP |
| P3 | **Transactional outbox.** A business change and its integration message commit together; a publisher delivers at least once. | [Outbox sequence](#85-transactional-outbox) |
| P4 | **Idempotency everywhere.** Deterministic workflow ids with `RejectDuplicate`, unique business keys, inbox tables, version-guarded upserts. | [Section 12](#12-reliability-idempotency-and-failure-modes) |
| P5 | **Keep the money-critical step synchronous and atomic.** The ERP order + stock reservation is one database transaction; Temporal orchestrates before and after it. | `ErpOrderService.createOrderWithReservation` |
| P6 | **Workflow definitions are language-neutral and separately deployable.** A Java orchestration worker hosts definitions; each service hosts only its own activities. | `temporal/workflows`, `contracts/schemas` |
| P7 | **No secrets or tokens in workflow history.** Activities receive `tenantId` in their input and act in-process. | [Security](#11-security-architecture) |
| P8 | **Tenant isolation is enforced in the service layer**, from the validated `tenant_id` claim. | `TenantContext`, every service query |
| P9 | **Invariants live in the database too.** CHECK and UNIQUE constraints back up application rules. | migrations 001-006, Flyway V1-V3 |

## 3. System context

![System context](diagrams/01-system-context.svg)

Users interact only with the CRM API. The ERP currently exposes only a health endpoint; all CRM-to-ERP interaction goes
through Temporal. Keycloak is the single identity provider.

## 4. Containers and deployment

![Container view](diagrams/02-containers.svg)

| Container | Technology | Responsibility | Task queue |
|---|---|---|---|
| `Crm.Api` | ASP.NET Core Minimal API, Foundgine 3.3.2, EF Core + Npgsql | REST API, business rules, outbox publisher, **hosts CRM activities** (when `Temporal:Enabled`) | `crm` |
| `erp` | Spring Boot, Spring JDBC, Flyway | ERP domain services, **hosts ERP activities** (when `temporal.enabled=true`) | `erp` |
| `OrchestrationWorker` | Java 21, Temporal Java SDK (no Spring) | Hosts workflow *definitions only* | `orchestration` |
| Temporal | auto-setup 1.25, namespace `enterprise` | Durable execution, history in `temporal-db` | n/a |
| Keycloak | 26 (dev mode), realm `enterprise` | Identity, roles, `tenant_id` claim | n/a |

![Deployment](diagrams/03-deployment.svg)

Local topology: three Postgres 17 instances (`crm-db` :5433, `erp-db` :5434, `temporal-db` internal), Temporal :7233
with UI :8233, Keycloak :8080, CRM :5000, ERP :8082. `./start.sh` brings up the infrastructure; the three applications run
on the host.

## 5. CRM internals

### 5.1 Components and request pipeline

![CRM components](diagrams/04-crm-components.svg)

Middleware order (from `Program.cs`): correlation id, exception handler, authentication, rate limiter, authorization,
endpoints. The rate limiter sits after authentication so the tenant claim can partition limits.

Foundgine is registered as a capability (`AddFoundgine(o => o.UseCrmDomain())`) that supplies the semantic metadata and
the EF provider. Its semantic authorization policy is allow-all; real authorization is role policies plus tenant scoping in
the services (decisions item 8). Schema is managed with plain SQL migrations applied at startup
(`Database:MigrateOnStart`), not EF migrations, because the EF model is generated from the Foundgine model.

### 5.2 HTTP API (`/api/v1`)

Roles: **read** = enterprise-admin, crm-admin, crm-sales, crm-support; **write** = enterprise-admin, crm-admin, crm-sales;
**admin** = enterprise-admin, crm-admin.

| Method and path | Access | Purpose |
|---|---|---|
| `GET /health` | anonymous | liveness |
| `GET /whoami` | any authenticated | subject, tenant, roles from the token |
| `POST/PUT /customers`, `GET /customers`, `GET /customers/{id}` | write / read | customers, search by `q`, `email`, `status`, `kind`, paging |
| `POST/GET /customers/{id}/contacts`, `GET /contacts/{id}` | write / read | contacts |
| `POST/GET /customers/{id}/opportunities`, `GET/PUT /opportunities/{id}` | write / read | opportunities |
| `GET /customers/{id}/sales-history` | read | Accepted and Ordered quotes |
| `GET /customers/{id}/integration` | read | ERP mapping state |
| `POST /opportunities/{id}/quotes`, `PUT /quotes/{id}/lines`, `GET /quotes/{id}` | write / read | quotes |
| `POST /quotes/{id}/submit`, `POST /quotes/{id}/accept` | write | quote lifecycle |
| `POST /quotes/{id}/approve`, `POST /quotes/{id}/reject` | **admin**, audited | segregated approval |
| `GET /quotes/{id}/fulfilment`, `GET /customers/{id}/invoices` | read | shipment, invoice, payment mirrors |
| `POST/GET /quotes/{id}/returns`, `GET /returns/{id}` | write / read | returns |
| `POST /integration/reconcile` | **admin**, audited | re-queue failed or stale customer syncs |
| `GET /audit` | **admin** | admin audit trail, newest first, tenant scoped |

### 5.3 CRM activities (hosted worker, queue `crm`)

| Activity | Used by | Effect |
|---|---|---|
| `crm.get-customer-for-sync` | available (not called by the current workflow) | returns the sync DTO |
| `crm.record-erp-customer` | onboarding | mapping to Active; different ERP id on an Active mapping is a conflict |
| `crm.mark-sync-failed` | onboarding | mapping to Failed with reason |
| `crm.record-erp-order` | quote-to-order | Accepted to Ordered; sets `erp_order_id`; conflict on a different id |
| `crm.mark-order-failed` | quote-to-order | Accepted to OrderFailed; no-op if already; conflict if Ordered |
| `crm.record-return-decision` | returns | Requested to Authorized or Rejected; Authorized to Completed |
| `crm.record-shipment`, `crm.record-invoice`, `crm.record-payment` | future ERP workflows | version-guarded upserts |

## 6. ERP and orchestration internals

![ERP and orchestration components](diagrams/05-erp-orchestration-components.svg)

| ERP activity (queue `erp`) | Service rule highlights |
|---|---|
| `erp.validate-and-create-customer` | pure `CustomerValidator`; idempotent insert on (tenant, CRM id); writes `customer_inbox`; non-retryable `ValidationFailed` |
| `erp.create-order-with-reservation` | one transaction: customer must be an Active ERP customer matching the CRM id; every SKU active; unit price not below `min_unit_price`; quote total equals the sum of lines; conditional `UPDATE ... WHERE on_hand - reserved >= qty` in SKU order; replay returns the existing Open order; a Cancelled order is refused. Non-retryable `ValidationFailed`, `InsufficientStock` |
| `erp.cancel-order` | compensation: order to Cancelled, release reservation; idempotent |
| `erp.authorize-return` | locks the order row (`FOR UPDATE`); returnable quantity per SKU = ordered minus other Authorized/Completed returns; business refusals are stored `Rejected` decisions |
| `erp.complete-return` | locks the return; the Authorized-to-Completed change gates a single `on_hand += qty` |

Workflows (`temporal/workflows`, queue `orchestration`):

| Workflow | Id | Query | Outcome values |
|---|---|---|---|
| `CustomerOnboardingWorkflow` | `CustomerOnboarding:<tenant>:<crmCustomerId>[:<attempt>]` | `status` | Completed, Failed |
| `QuoteToOrderWorkflow` | `QuoteToOrder:<tenant>:<quoteId>` | `status` | Completed, Failed |
| `ReturnProcessingWorkflow` | `ReturnProcessing:<tenant>:<returnId>` | `status`; signal `goodsReceived` | Completed, Rejected, AwaitingGoodsExpired |

![Quote-to-order workflow decision flow](diagrams/19-activity-quote-workflow.svg)

## 7. Integration contracts

Language-neutral JSON Schemas in `contracts/schemas/`, each with a `schemaVersion`:
`customer-sync.v1`, `erp-customer-activity.v1`, `quote-accepted.v1`, `order-activities.v1`, `mark-order-failed.v1`,
`fulfilment-activities.v1`, `return-requested.v1`, `return-activities.v1`. OpenAPI and event folders exist but are empty.

Retry policies (all activities: 30 s start-to-close, 1 s initial interval, backoff x2, 1 min cap):

| Workflow | Activity target | Max attempts | Not retried |
|---|---|---|---|
| Onboarding | ERP and CRM | 10 | `ValidationFailed`, `CrmNotFoundException`, `CrmValidationException` |
| Quote-to-order | ERP create | 10 | `ValidationFailed`, `InsufficientStock` |
| Quote-to-order | ERP cancel (compensation) | unlimited | `ValidationFailed` |
| Quote-to-order, Returns | CRM | unlimited (outages are waited out) | `CrmNotFoundException`, `CrmValidationException`, `CrmConflictException` |
| Returns | ERP | 10 | `ValidationFailed` |

Cross-cutting rules (`docs/conventions.md`): URL-major versioning (`/api/v1`), additive changes stay in a version, every
cross-service call carries `correlationId` and an idempotency key, no tokens in Temporal payloads.

## 8. Key flows

### 8.1 Customer onboarding

![Onboarding sequence](diagrams/12-seq-onboarding.svg)

### 8.2 Quote to order

![Quote to order sequence](diagrams/13-seq-quote-to-order.svg)

### 8.3 Returns

![Return sequence](diagrams/14-seq-return.svg)

### 8.4 Fulfilment mirrors

![Fulfilment mirrors](diagrams/17-seq-fulfilment-mirror.svg)

### 8.5 Transactional outbox

![Outbox](diagrams/16-seq-outbox.svg)

### 8.6 Recovery

![Reconcile](diagrams/18-seq-reconcile.svg)

## 9. State machines

![Quote lifecycle](diagrams/09-state-quote.svg)

![Mapping and opportunity](diagrams/10-state-mapping-opportunity.svg)

![Return and fulfilment mirrors](diagrams/11-state-return-fulfilment.svg)

## 10. Data architecture

Two independent PostgreSQL databases plus Temporal's own. No cross-database queries or foreign keys.

![Identifier ownership](diagrams/08-identifier-ownership.svg)

![CRM data model](diagrams/06-crm-data-model.svg)

![ERP data model](diagrams/07-erp-data-model.svg)

Design notes:
- Every CRM table carries `tenant_id`; unique business keys are per tenant.
- ERP ids come from ERP-owned sequences (`erp_customer_seq` 9001, `erp_order_seq` 5001, `erp_return_seq` 7001).
- `erp_inventory` has `CHECK (reserved <= on_hand)`, so over-reservation fails at the database even if application logic is wrong.
- Mirror tables use composite keys (`tenant_id`, ERP id) and a `version` column; writes apply only when the version increases.
- `admin_audit` is append-only (migration 005).

## 11. Security architecture

![Authentication sequence](diagrams/15-seq-auth.svg)

![Security layers](diagrams/23-security-layers.svg)

Keycloak realm `enterprise` (dev import, throwaway secrets):

| Client | Type | Notes |
|---|---|---|
| `crm-api`, `erp-api` | bearer-only | token audiences |
| `enterprise-web` | public, PKCE, password grant (dev) | audiences `crm-api` + `erp-api` |
| `crm-worker`, `erp-worker` | service accounts | role `integration-service`, own audience only |

Realm roles: enterprise-admin, crm-admin, crm-sales, crm-support, erp-admin, erp-sales, erp-inventory, erp-finance,
integration-service. Token claims: `sub`, `roles`, `tenant_id`, `aud`.

Known gaps: ERP has no JWT validation; service accounts carry no tenant; workflow signals are unauthenticated; no
secret rotation; role denials by policy are not audited; the in-process rate limiter is not shared across replicas.

## 12. Reliability, idempotency and failure modes

| Flow | Duplicate start | Duplicate/replayed activity | Conflict handling |
|---|---|---|---|
| Onboarding | workflow id `RejectDuplicate` | ERP unique (tenant, CRM id) + `customer_inbox`; CRM record is idempotent | different ERP id for an Active mapping: conflict |
| Quote-to-order | workflow id | ERP unique (tenant, quote) + `order_inbox`; replay reserves nothing; CRM record idempotent | different order id: conflict; cancelled order never returned as live |
| Return | workflow id | ERP unique (tenant, CRM return id); stock add gated by status change | different ERP return id or illegal transition: conflict |
| Mirrors | n/a | version must be greater than stored | stale ignored |

| Failure | Behaviour |
|---|---|
| CRM DB down during a request | request fails; no outbox row, no partial state |
| Temporal down | outbox rows stay unpublished; exponential backoff (cap 300 s); publishing resumes |
| ERP worker down | ERP activities retry (up to 10 attempts) then the workflow fails visibly |
| CRM worker down mid-workflow | CRM activities retry without limit; the workflow waits |
| ERP refuses (validation, price floor, stock) | no retry; nothing left behind; quote becomes `OrderFailed` with the reason |
| CRM permanently refuses an order | ERP order cancelled and reservation released; quote marked `OrderFailed` if the CRM allows |
| Duplicate or reordered mirror message | ignored by version guard |
| Goods never arrive | after 30 days result `AwaitingGoodsExpired`; return stays Authorized in both systems |
| Two concurrent orders for the last stock | conditional update lets exactly one win (tested in psql with 8 concurrent reservers against 10 units) |

## 13. Observability and operations

![Tracing](diagrams/20-seq-tracing.svg)

- Enable with `Telemetry:Enabled=true` and `OTEL_EXPORTER_OTLP_ENDPOINT`. Meter `Crm.Api`: `crm.outbox.published`,
  `crm.outbox.publish_failed`, `crm.admin.audit`, plus ASP.NET Core metrics.
- `X-Correlation-Id` is sanitised, echoed, placed in log scopes, spans and `admin_audit`.
- Suggested alerts (not wired): oldest unpublished outbox message over 5 min; mappings Failed or Pending over 15 min;
  returns `Requested` over 1 h; HTTP 429 and 5xx rates.
- Temporal UI (:8233, namespace `enterprise`) is the operational view of workflows.

## 14. Implementation status and open items

![Status by phase](diagrams/22-status-roadmap.svg)

Open items worth tracking (from `docs/decisions.md` plus observations from reading the code):

1. Compile and run everything; fix the SDK-call uncertainties in decisions items 12, 17, 18, 19.
2. ERP REST endpoints, product and inventory management, Keycloak JWT on the ERP.
3. ERP fulfilment producers: shipment, invoice, payment, replenishment workflows.
4. `QuoteApprovalWorkflow`, user-initiated order cancellation, tax calculation, retry of `OrderFailed` quotes.
5. Workflow-signal authorization; operator path for `AwaitingGoodsExpired`; reconcile endpoint for returns.
6. **Java version drift:** the plan says Java 21, `erp/pom.xml` and CI use 27, orchestration uses 21.
7. **Doc drift:** `docs/customer-onboarding.md` still says the Java workflow is "not yet written"; it exists.
8. Observations to verify when the code runs: the onboarding workflow does not list `CrmConflictException` as
   non-retryable (a conflict would retry up to 10 times before failing), and its `crm.mark-sync-failed` call is not guarded,
   so a failure there fails the workflow instead of returning `Failed`.

## 15. Glossary

| Term | Meaning |
|---|---|
| Activity | a retryable unit of work executed by a worker on a task queue |
| Compensation | an activity that undoes an earlier step (`erp.cancel-order`) |
| Foundgine | the semantic-model runtime used by the CRM (and planned for the ERP) |
| Inbox | a table recording processed message keys so repeats are harmless |
| Mapping | the CRM table relating a CRM customer id to an ERP customer id |
| Outbox | a table written in the business transaction and drained by a publisher |
| Task queue | a named queue workers poll: `orchestration`, `crm`, `erp` |
| Tenant | the isolation boundary, carried as the `tenant_id` claim |
