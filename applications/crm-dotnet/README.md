
# Foundgine CRM — Keycloak starter

This is a runnable CRM starter containing:
- ASP.NET Core 9 API
- PostgreSQL via Docker Compose
- Keycloak OIDC login (Authorization Code + PKCE in the browser)
- React + TypeScript frontend
- Tenant-scoped companies, contacts and opportunities
- Role-based API policies
- Read-only AI chat assistant backed by a configurable OpenAI-compatible Chat Completions endpoint

## Status

CRM reads and writes go through **Foundgine Runtime 2.3.1** and the concrete SQL/PostgreSQL providers in
**Foundgine.Providers 2.3.1**. The API now registers `IFoundgineExecutor` and `IFoundgineMutations`
through `AddFoundgine`; CRM code submits `SemanticRequest` and `SemanticMutationRequest` rather than
constructing its own authorization/planning/lowering pipeline. The runtime owns semantic authorization,
planning, security certification and provider dispatch.

The API project references Foundgine NuGet packages at **2.3.1**. The Java ERP uses the same 2.3.1 release; confirm the source archive and package release are compatible before relying on analyzer/API parity. The `Foundgine.Extensions` 2.3.1 NuGet package is a **Hot Chocolate GraphQL integration**,
not a PostgreSQL persistence package. The CRM references it for the authenticated `/graphql` endpoint
and uses its built-in GraphQL-to-semantic adapter and secure query executor. PostgreSQL SQL execution
and batched mutations are supplied by `Foundgine.Providers`; there is no separate PostgreSQL extension
package in this source layout.

The CRM uses the `Foundgine.Providers` AOT analyzer bundled with the provider package.
`GeneratedMetadata.Build()` supplies structural metadata, and a typed CRM overlay adds business
vocabulary and constraints. A request-scoped, async-flow-local policy adapter supplies the validated
Keycloak tenant/role context to the singleton Foundgine Runtime policy without sharing identities
between concurrent requests. The SQL execution context separately binds `context.TenantId`, so the
tenant predicate remains parameterized and execution fails closed when the context value is absent.

**EF Core owns schema lifecycle only.** `backend.Migrations/` contains a separate EF Core 9 / Npgsql 9
migration project with a schema-only model and an initial EF migration source/snapshot. The initial
migration is supplied in EF's standard migration format; it has not been scaffolded or validated by
`dotnet ef` in this environment because the .NET SDK is unavailable. The API does not reference EF Core
and continues to execute all CRM reads/writes through Foundgine's SQL provider. `NpgsqlDataSource` is
used only as the PostgreSQL connection factory; it does not compile SQL or implement a parallel CRUD
repository. The PostgreSQL relational execution provider is enabled; only the optional vector-retrieval
capability is not enabled because this CRM currently has no vector-search use case. This separation avoids
forcing the EF provider's Npgsql 9 dependency into the API's Foundgine/Npgsql 10 dependency graph.
The initial EF migration creates `crm_*` tables, tenant-aware composite foreign keys, indexes, an
opportunity amount check constraint, and a separate `keycloak` schema. The EF schema model is
intentionally isolated from Foundgine's annotated domain records to avoid dependency-version coupling;
add schema-parity tests so the two explicit mappings cannot silently drift. The Keycloak schema is owned
by Keycloak; EF only creates the empty schema so Keycloak can initialise its tables.

**Checked in this environment:** archive/configuration integrity and static source checks only. The
container does not have the .NET SDK, so this revision has **not** been compiled, restored, or tested.
The earlier low-level recording-fake harness is retired because the application now uses Runtime
interfaces and PostgreSQL provider adapters. See `verification/` for the current build/test gates. **Not checked:**
Foundgine generator execution, PostgreSQL, Keycloak/JWT login, `docker compose up`, frontend, or Native AOT publish.

The AWS deployment baseline is now Terraform + ECS Fargate + ALB + RDS PostgreSQL + ECR + Secrets Manager.
It is a deployable starter architecture, not a completed production security review. The AWS starter
uses one RDS database and one database master credential shared by the CRM and Keycloak schemas; split
those into separate databases/least-privilege users before handling production customer data. Use HTTPS
with an ACM certificate and custom domain. Without a certificate, Terraform intentionally falls back to
HTTP for evaluation only. Terraform state contains secret material and must be stored in an encrypted,
access-controlled remote backend before team/production use.

## AI assistant

The UI includes a floating CRM assistant. Copy `.env.example` to `.env`, add `OPENAI_API_KEY`, then run `docker compose up --build`; `OPENAI_MODEL` and `OPENAI_CHAT_COMPLETIONS_URL` can override the defaults. A blank key leaves the rest of the CRM usable but the chat endpoint returns HTTP 503. optionally set `OPENAI_MODEL` and `OPENAI_CHAT_COMPLETIONS_URL` for a compatible provider. The key is injected into the API container only and is never exposed to the browser. Chat history is request-scoped and not persisted. The assistant is read-only: it can invoke only `crm_query`, which calls Foundgine's MCP query adapter and is re-authorized against the signed-in Keycloak subject, tenant and field grants. Writes remain in the normal CRM UI/API path.

## Foundgine PostgreSQL provider package

For Foundgine v2.3.1, use `Foundgine.Providers` for the concrete storage provider integrations.
The v2 package consolidates the former `Foundgine.Sql`, `Foundgine.InMemory`, `Foundgine.Elasticsearch`,
and `Foundgine.Postgres.Vector` packages. Its NuGet package metadata lists SQL storage and PostgreSQL/vector
integrations and declares Npgsql as a dependency. Do not add a guessed package such as
`Foundgine.PostgreSQL` or `Foundgine.Persistence.PostgreSQL`.

This CRM registers `SqlCompiler` as the Runtime's `IProviderPlanCompiler`, uses a pooled adapter
around `SqlExecutionProvider` for reads, and a pooled adapter around
`PostgresBatchedMutationExecutionProvider` for writes. The PostgreSQL relational provider is active;
the optional vector-retrieval capability is not needed for this CRM. `NpgsqlDataSource` is connection
infrastructure only; semantic authorization, planning, mutation lowering and SQL execution stay in Foundgine.

The EF Core project remains a schema/migration tool only. It does not become the CRM runtime repository.

## How the integration works

- `backend/Domain/CrmDomain.cs` - persistence/domain records annotated for Foundgine's AOT metadata generator.
- `backend/Foundgine/CrmModel.cs` - consumes `GeneratedMetadata.Build()`, composes the semantic overlay and freezes the contract.
- `backend/Foundgine/CrmSemanticOverlay.cs` - CRM aliases, field capabilities and semantic constraints.
- `backend/CrmJsonContext.cs` - compile-time JSON serialization metadata; reflection-based JSON fallback is disabled.
- `backend/Foundgine/CrmAuthorization.cs` - per-request tenant and role policy. Tenant isolation is a predicate rule,
  `resource.TenantId == context.TenantId`, which the SQL provider lowers to `WHERE tenant_id = @auth0`
  with the value taken from the validated `tenant_id` token claim.
- `backend/Foundgine/CrmSemanticExecutor.cs` - translates CRM reads to `SemanticRequest` and writes to `SemanticMutationRequest`, then calls `IFoundgineExecutor` / `IFoundgineMutations`. The Runtime owns authorization, planning, security certification and execution dispatch.
- `backend/Foundgine/CrmRuntimeAuthorizationPolicy.cs` - bridges validated request scopes to the singleton Runtime policy using `AsyncLocal`; bootstrap access is sealed after both runtime engines are constructed.
- `backend/Foundgine/CrmRuntimeProviders.cs` - connection-scoped adapters over `SqlExecutionProvider` and `PostgresBatchedMutationExecutionProvider`.
- `backend/FoundgineCrmStore.cs` - the `ICrmStore` the endpoints use.

## Semantic contract and discovery

`GET /api/semantic/capabilities` publishes the versioned `crm.semantic.v1` contract: entity names,
field descriptions, supported operations and security invariants. This endpoint is intentionally
schema-only. It is not a capability token and must never be used as a substitute for authorization.
The runtime executor re-authorizes every read and write using the authenticated request's tenant and
roles. `CrmSemanticCatalog` validates that its field declarations match the generated `CrmModel` field IDs.
The capability-contract endpoint is policy-aware: tenant predicates are reported as conditional access,
`TenantId` is omitted as a caller-selectable field, and sensitive contact fields are hidden from read-only roles.

The current request boundary is deliberately narrow: REST endpoints map business requests to typed
Foundgine semantic operations; no caller can submit raw SQL, metadata IDs, tenant predicates or provider
instructions. `/api/semantic/capability-contract` uses Foundgine capability discovery with the current
request's policy, so denied/conditional access is described without exposing executable tenant predicates.

## Known gaps

1. **Build-time verification remains outstanding.** The API references `Foundgine.Providers` 2.3.1,
   whose source project packs `Foundgine.Providers.Aot.Generator` under `analyzers/dotnet/cs`. The CRM
   domain records use the sample's attributes and `CrmModel` consumes `Foundgine.Generated.GeneratedMetadata`.
   Run restore/build and assert the generated source under `backend/obj/Generated` before treating this as
   verified. The current environment has no .NET SDK. A browser E2E suite and PowerShell runner are provided under `frontend/e2e` and `verification/run-e2e.ps1`, but they have not been executed in this environment.
2. **Mutation tenant ownership remains host-enforced.** Foundgine's mutation authorizer does not apply
   read predicates to writes. Creates stamp `TenantId` from the validated host scope and reject caller
   supplied tenant values; the database enforces `(tenant_id, company_id)` foreign keys. Any future
   update/delete must add an explicit tenant filter or equivalent tenant-bound mutation condition.
3. **AI chat is read-only and configuration-dependent.** Set `OPENAI_API_KEY` before starting Compose.
   The model can invoke only `crm_query`, which delegates to the existing Foundgine MCP query tool
   under the current Keycloak request context. No write tool, raw SQL, tenant argument, or database
   credential is exposed to the model. The in-memory warrant signer and replay store remain demo-only
   and must be replaced before multi-replica/production deployment.
4. Optional `Contact.CompanyId` is retained as a nullable semantic dimension but is not yet a generated graph relationship: the current AOT generator requires exact FK/PK CLR type equality (`Guid?` versus `Guid`). The required `Opportunity.Company` and reverse `Company.Opportunities` relationships are generated and authorized. Mutation execution in the SQL provider is synchronous, so each write blocks a thread briefly.
5. Native AOT publishing is opt-in and has not been built in this environment. The API uses source-generated
   JSON metadata, but the complete dependency graph must still pass the AOT analyzer and publish gates.
6. Existing dev volumes may contain the old `Companies`/`Contacts`/`Opportunities` tables; migrations
   create the `crm_*` schema. Use `docker compose down -v` for a clean disposable local reset.
7. The AWS starter puts the CRM tables in `public` and Keycloak tables in `keycloak`, but currently
   shares the RDS master role. Create separate roles and rotate secrets before production.

## Requirements
- Docker Desktop / Docker Compose
- .NET 10 SDK (only if running API/tests or EF migrations outside Docker)
- Node.js 20+ (only if running frontend outside Docker)
- For AWS: AWS CLI v2, Terraform >= 1.6, Docker, jq, and credentials with VPC/EC2, ECS, ECR,
  RDS, ELB, IAM, CloudWatch Logs, Secrets Manager and (optionally) Route 53 permissions.

## EF Core schema and migrations
From the repository root, with .NET 10 installed:

```bash
cd backend.Migrations
dotnet tool restore
dotnet ef migrations list
dotnet ef migrations script --output ../infra/postgres/initial-schema.sql
# For a local database, configure ConnectionStrings__Crm first, then:
dotnet ef database update
```

The API does not call `EnsureCreated` or execute schema DDL at startup. Local Compose runs the EF
migration bundle as a one-shot `migrations` service before starting the API. For future model changes,
edit `CrmDbContext`, then scaffold a new migration:

```bash
dotnet ef migrations add DescribeYourChange
```

Review generated migration SQL before applying it. Never use `EnsureCreated` alongside migrations.

## Start locally
From this directory:

```bash
docker compose up --build
```

The default container uses the JIT runtime. To test the Native AOT image (Linux x64):

```bash
ENABLE_NATIVE_AOT=true API_IMAGE_TARGET=aot docker compose build api
ENABLE_NATIVE_AOT=true API_IMAGE_TARGET=aot docker compose up
```

For local AOT publishing from `backend/` (requires the .NET 10 AOT workload/toolchain):

```bash
dotnet publish -c Release -r linux-x64 -p:EnableNativeAot=true -p:SelfContained=true
```

AOT is deliberately explicit: a successful JIT build is not evidence that Native AOT is supported. Treat
all trim/AOT warnings as release blockers and test the published executable in the target container.

Open:
- CRM: http://localhost:3000
- API health: http://localhost:5080/health
- Keycloak: http://localhost:8080

Demo Keycloak administrator:
- username: `admin`
- password: `change-me-now`

Import realm: `foundgine-crm` is imported automatically on first Keycloak startup.

Demo user:
- username: `sales`
- password: `ChangeMe123!`
- realm role: `sales-representative`
- tenant claim: `tenant-demo`

The frontend logs in through Keycloak. API requests require a bearer token and a `tenant_id` claim. The API derives tenant scope from the signed token, not from query/body input.

## Keycloak client setup
The imported realm defines:
- public client `crm-web` with standard flow and PKCE S256
- bearer-only confidential API client `crm-api`
- realm roles: `crm-admin`, `sales-manager`, `sales-representative`, `crm-readonly`

For machine-to-machine integration, create a dedicated confidential client with service accounts and assign only the required roles. Do not let an AI agent choose its tenant or user identity in request arguments.

## Deploy to AWS with Terraform

The Terraform stack provisions:
- VPC with two public and two private subnets; the ALB is public and RDS is private.
- ECS Fargate services for the .NET API, React/Nginx frontend and Keycloak.
- Application Load Balancer routing `/api/*` and `/health` to the API, `/auth/*` to Keycloak,
  and all other paths to the frontend.
- Encrypted RDS PostgreSQL, ECR repositories, CloudWatch log groups, generated Secrets Manager
  credentials, and IAM task execution permissions.
- A one-shot EF migration task. The deploy script waits for it to finish successfully before
  forcing the ECS services to roll out the new containers.

**Quick deployment** (from the repository root):

```bash
cp infra/terraform/terraform.tfvars.example infra/terraform/terraform.tfvars
# Edit terraform.tfvars. For a production URL, set domain_name, certificate_arn and hosted_zone_id.
aws configure
./scripts/deploy-aws.sh
```

Default region is `ap-southeast-2` (Sydney). The script builds Linux/amd64 images, pushes them to
ECR, runs the EF migration bundle against RDS, then waits for the ECS services to stabilise. It
prints the CRM URL and a command to retrieve the generated Keycloak administrator password from
Secrets Manager. The AWS realm import deliberately omits the local demo `sales` user/password.

For HTTPS, request/validate an ACM certificate in the same AWS region and provide:

```hcl
domain_name     = "crm.example.com"
certificate_arn = "arn:aws:acm:ap-southeast-2:123456789012:certificate/your-certificate-id"
hosted_zone_id  = "Z0123456789EXAMPLE"
```

The Route 53 alias is created when `hosted_zone_id` is supplied. Without a certificate, the stack
uses HTTP and must only be used for short-lived evaluation. The ECS tasks run in public subnets with
public IPs to avoid NAT Gateway cost; security groups allow inbound application traffic only from
the ALB, and RDS remains in private subnets. For stricter production isolation, move tasks to private
subnets and add VPC endpoints or NAT, separate Keycloak and CRM database roles/databases, configure
remote encrypted Terraform state, add WAF/rate limits, and review backup/restore and alerting.

To request a Native AOT API image build (Linux x64), set `ENABLE_NATIVE_AOT=true` before running
the deploy script. This is not proof that the Foundgine dependency graph is AOT-clean: treat the
publish as a gate and inspect every warning. The default deploy is JIT.

To destroy the evaluation stack, first set `deletion_protection = false` on the RDS resource in
`infra/terraform/main.tf` (and apply), then run `terraform destroy`. Retain or snapshot any data you
need before teardown. Terraform state includes generated database credentials; never commit it.

## API endpoints
- `GET /api/companies`
- `POST /api/companies`
- `GET /api/contacts`
- `POST /api/contacts`
- `GET /api/opportunities`
- `POST /api/opportunities`
- `GET /api/semantic/capabilities` (authenticated, versioned schema catalog)
- `GET /api/semantic/capability-contract` (authenticated, Foundgine capability discovery with request policy)
- `POST /graphql` (authenticated, Foundgine Hot Chocolate semantic query adapter)
- `GET /health`

Run semantic-model tests with `dotnet test backend.Tests/Foundgine.Crm.Semantic.Tests.csproj`.

All CRM records are filtered by the authenticated `tenant_id`. Create operations assign the tenant from the token.

## Next steps
1. Run `dotnet ef migrations list` and `dotnet ef database update` against a clean PostgreSQL database.
2. Restore/build the API and verify `Foundgine.Generated.GeneratedMetadata` is emitted.
3. Run `dotnet test backend.Tests/Foundgine.Crm.Semantic.Tests.csproj` and add real PostgreSQL tests for tenant isolation, role denial and cross-tenant FKs.
4. Run `terraform -chdir=infra/terraform init -backend=false` and `terraform -chdir=infra/terraform validate`, then review a real plan before AWS deployment.
5. Run Native AOT publish and container smoke tests.
6. Implement tenant-filtered update/delete operations, then design warrant issuance before adding MCP/AI tools.

## GraphQL API (Foundgine Hot Chocolate adapter)

The API exposes `POST /graphql` using the built-in `Foundgine.Extensions` 2.3.1 Hot Chocolate
adapter and secure query executor. This is an ASP.NET Core GraphQL-over-HTTP adapter endpoint: it
uses Hot Chocolate's GraphQL parser through Foundgine's adapter, but does not host the separate
`HotChocolate.AspNetCore` schema server. It translates GraphQL operations into Foundgine semantic
requests and does not create a second ORM or bypass Runtime authorization.

Example:

```http
POST /graphql
Authorization: Bearer <keycloak-token>
Content-Type: application/json
```

```json
{
  "query": "query { company { id name industry } }"
}
```

The current CRM host exposes authenticated scalar root-field queries. The Foundgine adapter currently
requires one root field and does not accept root arguments until the semantic query argument contract
is implemented. Nested relationship selections are rejected by the CRM response materializer rather
than returned with an incorrect shape. GraphQL mutations are not exposed yet: tenant-scoped mutation
warrants and mutation result materialization must be completed and tested before enabling writes.

The `/graphql` route is included in the AWS Application Load Balancer API target routing.

