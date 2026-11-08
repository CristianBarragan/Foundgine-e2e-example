# Enterprise platform

CRM (.NET 10 + Foundgine) and ERP (Java + Spring Boot + Foundgine) integrated through Temporal, with Keycloak identity.
See `docs/implementation-plan.txt` and `docs/decisions.md`.

## Run
    ./start.sh                              # infrastructure (needs Docker)
    dotnet run --project crm/src/Crm.Api    # http://localhost:5000/api/v1/health
    mvn -f erp/pom.xml spring-boot:run      # http://localhost:8082/api/v1/health

## Test
    dotnet test crm/Crm.slnx
    mvn -f erp/pom.xml verify

## CRM (Phase 3)
    ./start.sh
    export CRM_TEST_CONNECTION="Host=localhost;Port=5433;Database=crm;Username=crm;Password=crm_dev_pw"
    dotnet test crm/Crm.slnx          # DB-backed tests skip when CRM_TEST_CONNECTION is unset
    ASPNETCORE_ENVIRONMENT=Development dotnet run --project crm/src/Crm.Api
Dev auth headers: `X-Tenant-Id`, `X-User-Id`, `X-Roles` (e.g. `crm-sales`). See `docs/decisions.md`.

## Phase 8 (CRM side)
Shipment/invoice/payment status mirrors, returns. See `docs/fulfilment.md`.

## Phase 9 (CRM side, partial)
Rate limiting, correlation ids, admin audit trail (`GET /api/v1/audit`). See `docs/decisions.md` item 16.

## ERP customer onboarding (Java)
    ./start.sh
    mvn -f erp/pom.xml spring-boot:run -Dspring-boot.run.arguments=--temporal.enabled=true   # ERP + erp activities
    mvn -f temporal/workflows/pom.xml exec:java                                              # orchestration worker
    ASPNETCORE_ENVIRONMENT=Development dotnet run --project crm/src/Crm.Api                  # with Temporal:Enabled=true
    ERP_TEST_JDBC="jdbc:postgresql://localhost:5434/erp?user=erp&password=erp_dev_pw" mvn -f erp/pom.xml verify
Create a customer in the CRM; the outbox starts `CustomerOnboardingWorkflow`, the ERP creates its customer, the CRM mapping becomes Active.

## Keycloak + Temporal providers (CRM)
    dotnet run --project crm/src/Crm.Worker   # Foundgine process worker (foundgine) + C# workflow worker (crm-orchestration)
See `docs/keycloak.md` (last section) and `docs/decisions.md` item 24.

## Phase 7 (Java side): quote-to-order
ERP order + atomic stock reservation, `QuoteToOrderWorkflow`, compensation. See `docs/quote-to-order.md` and `docs/decisions.md` item 19.
The Java orchestration worker hosts Java workflow types on `orchestration`; the C# customer-to-cash worker polls `crm-orchestration`. The ERP (with `--temporal.enabled=true`) hosts ERP activity sets.

## Phase 8 (Java side, returns)
ERP return authorization/completion and `ReturnProcessingWorkflow` (waits for a `goodsReceived` signal, sent by the authenticated ERP endpoint `POST /api/v1/returns/{id}/goods-received`). See `docs/fulfilment.md`, decisions items 20-21 and 26.



## Customer-to-cash Temporal workflow (checkpoint 11)

The CRM quote-accepted outbox starts the C# `CustomerToCashWorkflow` on the shared `orchestration` queue. The C# worker coordinates approval and starts Java `QuoteToOrderWorkflow` as a child. Java remains the sole owner of ERP order creation, inventory reservation, CRM order linking, and compensation. See [docs/customer-to-cash-temporal.md](docs/customer-to-cash-temporal.md).


## Latest increment

See [CHECKPOINT-12.md](CHECKPOINT-12.md) for the C# fulfilment lifecycle coordinator and ERP transactional Temporal event relay.

Latest implementation increment: [Checkpoint 13](CHECKPOINT-13.md) adds executable ERP shipment, invoicing, and payment operations with transactional Temporal outbox publication.


Latest implementation increment: [Checkpoint 14](CHECKPOINT-14.md) hardens single-writer CRM projections and ERP command idempotency/retry behavior.
