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

## Phase 7 (Java side): quote-to-order
ERP order + atomic stock reservation, `QuoteToOrderWorkflow`, compensation. See `docs/quote-to-order.md` and `docs/decisions.md` item 19.
The orchestration worker now hosts both workflows; the ERP (with `--temporal.enabled=true`) hosts both activity sets.

## Phase 8 (Java side, returns)
ERP return authorization/completion and `ReturnProcessingWorkflow` (waits for a `goodsReceived` signal). See `docs/fulfilment.md`, decisions items 20-21.

