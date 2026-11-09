# Verification

## Build and test gates

Run from the starter root with the .NET 10 SDK installed:

```bash
dotnet restore backend/Foundgine.Crm.Api.csproj
dotnet build backend/Foundgine.Crm.Api.csproj -c Release
dotnet test backend.Tests/Foundgine.Crm.Semantic.Tests.csproj -c Release
dotnet tool restore --tool-manifest backend.Migrations/.config/dotnet-tools.json
dotnet ef --project backend.Migrations/Foundgine.Crm.Migrations.csproj migrations list
```

The API project references Foundgine 2.3.1 `Core`, `Runtime`, and `Providers`. The `Providers` NuGet
package carries the AOT generator analyzer. Build output must include generated
`Foundgine.Generated.GeneratedMetadata` source under `backend/obj/Generated`.

The semantic tests cover generated metadata, schema mappings, generated relationships, semantic
overlay composition, tenant authorization, sensitive-field visibility, capability discovery and
request-scope isolation of the singleton Runtime authorization policy.

## PostgreSQL integration gates

Run against a disposable PostgreSQL instance with two test tenants. Verify:
- reads cannot cross tenant boundaries, including relationship traversals;
- read-only roles cannot select sensitive contact fields;
- read-only roles cannot mutate;
- a tenant cannot reference another tenant's company;
- all creates stamp tenant identity from the host request scope;
- mutation execution is atomic and returns the expected generated/returned values.

The previous recording-fake harness targeted the pre-Runtime low-level executor and has been removed
rather than retained as misleading evidence. This environment has no .NET SDK, so no .NET build,
test, EF migration, PostgreSQL, Docker, Keycloak, Terraform or Native AOT execution is claimed.


## Dynamic REST and MCP validation gates

- [ ] Restore `ModelContextProtocol` and `ModelContextProtocol.AspNetCore` 2.2.0.
- [ ] Build the API and verify `FoundgineMcpTools` is resolved with the contract-backed lexical grounder.
- [ ] Authenticate with a Keycloak token containing `tenant_id`; unauthenticated `/mcp` and `/api/semantic/execute` calls must be rejected.
- [ ] Execute `foundgine_capabilities`, `foundgine_query`, and `foundgine_query_semantic` over `/mcp`.
- [ ] Execute `POST /api/semantic/execute` with both natural-language and structured JSON intents.
- [ ] Verify tenant A cannot read tenant B rows through either transport.
- [ ] Verify read-only users cannot select `Contact.Email` or `Contact.Phone` through either transport.
- [ ] Verify request body/tool arguments cannot set subject, tenant, authorization predicates, SQL, provider, or connection details.
- [ ] Verify `/mcp*` reaches the API target group through the AWS ALB.
- [ ] Replace the in-memory warrant signer and replay store before multi-replica/production deployment.
- [ ] Verify Native AOT publish separately; MCP SDK transport/tool discovery may add trimming/AOT requirements.

Dynamic mutations are intentionally not exposed over REST or MCP in this revision. Add them only after host-side tenant stamping and tenant-bound update/delete filters are implemented and tested.


## Browser-to-database E2E

A Playwright suite is provided under `frontend/e2e`. It expects the complete Compose stack to be running at `http://localhost:3000`, `http://localhost:5080` and `http://localhost:8080`. Install the frontend dependencies and browser once with `npm install` and `npx playwright install chromium`, then run `npm run test:e2e` from `frontend/`. Alternatively run `./verification/run-e2e.ps1` from PowerShell to start an isolated Compose project (`foundgine-crm-e2e`), wait for readiness, install Chromium and execute the suite. Stop it with `docker compose -p foundgine-crm-e2e down`. The suite signs in through the real Keycloak browser flow, creates a company as tenant A, and verifies the company is not visible after signing in as tenant B. The AI chat test runs only when `OPENAI_API_KEY` is set and verifies the UI-to-model-to-Foundgine query path. These are executable gates, not claims that this archive has already passed them.

Before release, also run the PostgreSQL cross-tenant, sensitive-field and write-denial checks above. The Playwright tenant test is a black-box check of the key isolation invariant, but does not replace the lower-level security tests.
