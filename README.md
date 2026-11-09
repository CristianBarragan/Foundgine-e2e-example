# Foundgine Enterprise — CRM + ERP Integration Baseline

This repository packages the two supplied applications under one enterprise architecture
without merging their languages, runtimes, domain models, databases, or deployment lifecycles.

## Repository map

```text
foundgine-enterprise/
├── foundgine/                  # Shared semantic boundary design and adapter contracts
├── applications/
│   ├── crm-dotnet/             # Original C#/.NET CRM project, including React web frontend
│   └── erp-java/               # Original Java supply-chain project (preserved)
├── integration/
│   ├── contracts/              # API and event contracts
│   ├── adapters/               # Adapter ownership and mapping specifications
│   └── workflows/              # Cross-domain workflow specifications
├── identity/keycloak/          # Identity integration decisions
├── deployment/                 # Cross-application deployment guidance
└── tests/                      # Enterprise integration test plan
```

## What is included

- The complete contents of both original application ZIP archives, preserved under `applications/`.
- The separately supplied React CRM frontend archive, verified byte-for-byte against the CRM frontend and documented at `applications/crm-dotnet/frontend/README.md`.
- A shared Foundgine boundary specification describing semantic discovery, query planning,
  mutation dry-run, approvals, policy enforcement, tenant isolation, execution and audit.
- An OpenAPI description of the CRM HTTP endpoints documented by the supplied CRM project.
- Versioned JSON Schema event contracts for an inventory reservation workflow.
- A mapping and security specification for the CRM and ERP adapters.
- A repository-level implementation and verification plan.

## Important implementation status

This is an **integration baseline**, not a claim that all cross-application runtime integration
has already been implemented.

The supplied CRM embeds Foundgine .NET packages (2.3.1) in its own API process. The supplied
Java ERP uses Foundgine Java artifacts (2.3.1) inside its own process and exposes an MCP server.
There is no independently deployable shared Foundgine gateway in either archive.

The CRM archive includes a Keycloak realm and configures CRM authentication against that realm.
The Java sample enforces tenant/role policy in its own execution pipeline, but the archive does
not demonstrate Keycloak token validation or shared SSO between CRM and ERP.

The ERP archive's Maven POM inherits a parent POM outside the archive (`../../pom.xml`); that
parent is not included. The ERP project therefore needs a standalone build definition or must
be built from its original Foundgine multi-module repository. This package does not silently
replace its dependency graph.

The event schemas in `integration/contracts/events/` are **proposed contracts**. The supplied
projects do not yet publish or consume these events through an event bus.

## Architectural invariants

1. CRM remains authoritative for customer, contact, lead, opportunity and sales data.
2. ERP remains authoritative for product, inventory, procurement, supplier, warehouse and
   fulfillment data.
3. No shared database and no cross-language domain references.
4. Integration occurs through versioned HTTP/MCP contracts and asynchronous domain events.
5. Foundgine owns semantic planning and execution policy; it does not become a second CRM or ERP.
6. Every application independently owns its build, tests, secrets, database migrations and release.
7. Cross-system writes use an idempotent saga/process manager; no distributed database transaction.

## CRM web frontend

The React 19 / TypeScript / Vite frontend is included at `applications/crm-dotnet/frontend/`. It has its own Dockerfile, Nginx configuration, Keycloak JS integration, and Playwright E2E test. The CRM Compose `web` service builds from `./frontend`. See its README for commands and deployment ownership. This is the CRM UI, not yet a separate enterprise-wide AI/experience portal.

## Current capabilities in the supplied source

| Capability | CRM (.NET) | ERP (Java) |
|---|---|---|
| REST | CRM CRUD and semantic endpoints | Not shown as a general REST business API |
| MCP | Foundgine MCP tools hosted by ASP.NET Core | JSON-RPC MCP server at `/mcp` |
| GraphQL | Limited Foundgine-backed query adapter | Not shown |
| Persistence | PostgreSQL via Foundgine provider; EF migrations isolated | PostgreSQL-backed order mutation store plus in-memory sample |
| Authorization | Keycloak JWT + tenant-scoped CRM policy | Tenant/role policy and validated narrowing claims |
| Mutations | CRM create operations; GraphQL mutations not enabled | `place_order` / `cancel_order` with idempotency |
| Event bus | Not present in supplied archive | Not present in supplied archive |

## Backend completion work in progress

The ERP Maven project has been converted to a standalone build definition in `applications/erp-java/pom.xml`; see `applications/erp-java/BUILDING.md`. The missing external parent POM is no longer a project-level dependency. This packaging change is not yet build-verified.

The backend is **not yet production-complete**. In particular, ERP's MCP HTTP transport still needs Keycloak JWT/JWKS validation, no operational outbox/broker/inbox is wired, and the cross-domain saga needs a real CRM commercial-order trigger. See `docs/backend/COMPLETION-STATUS.md` for the exact remaining acceptance gates and the business decision needed before implementing the order saga.

## Build and verification

The authoring environment has Java 21 but does not have `dotnet`, `mvn`, or Docker installed. No build, container, migration, or end-to-end test is claimed as passing.

CRM tests (run from `applications/crm-dotnet`):
```bash
dotnet test backend.Tests/Foundgine.Crm.Semantic.Tests.csproj
```

ERP tests must be run with the original Foundgine Maven parent available, or after the ERP POM is
made standalone:
```bash
mvn test
```

See `docs/IMPLEMENTATION-ROADMAP.md` for the next implementation increments.


## Durable workflow orchestration

Cross-domain processes are orchestrated by **Temporal in C#**. The independent worker/API projects and the initial commercial-order saga are in [`orchestration/`](orchestration/README.md). Temporal coordinates long-running business processes; Foundgine remains the shared semantic execution boundary, while CRM and ERP retain their own bounded contexts and databases.
