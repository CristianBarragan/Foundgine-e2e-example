# AOT and semantic execution notes

## Foundgine integration pattern

The CRM now follows the `Foundgine.SupplyChain.Advanced/Semantic` sample from the supplied Foundgine source:

1. `backend/Domain/CrmDomain.cs` declares persistence/domain types with `[FoundgineEntity]`, `[FoundgineField]`, `[FoundgineSemanticDimension]` and `[FoundgineRelationship]`.
2. `Foundgine.Providers` 2.3.1 carries the Roslyn analyzer from `Foundgine.Providers.Aot.Generator`. The package project builds and packs it under `analyzers/dotnet/cs`; there is no separate runtime generator dependency.
3. The analyzer emits `Foundgine.Generated.GeneratedMetadata`. `CrmModel` calls `GeneratedMetadata.Build()` and consumes the resulting structural catalog.
4. `CrmSemanticOverlay` adds business vocabulary, field capabilities and constraints without recreating structural entity/field/relationship metadata.
5. `CrmModel` composes the generated catalog and semantic overlay. `AddFoundgine` freezes the model and creates the immutable runtime contract at startup.
6. `AddFoundgine` registers `IFoundgineExecutor` and `IFoundgineMutations`. `CrmSemanticExecutor` translates CRM operations to `SemanticRequest` / `SemanticMutationRequest`; Foundgine Runtime performs authorization, planning, security certification and dispatch to the SQL/PostgreSQL providers.

## Security invariants

- Tenant identity comes only from the validated `tenant_id` token claim. It is not accepted from route, query, body or semantic mutation input.
- Reads apply `resource.TenantId == context.TenantId`, with the tenant bound as `context.TenantId` in the execution context. `TenantId` is not a caller-selectable read field; response DTOs receive it from the authenticated scope.
- Contact email/phone fields are marked semantically sensitive. The policy hides them from read-only principals and the store omits them from those read projections.
- Create operations reject a caller-supplied `TenantId` and stamp it from the authenticated scope. The mutation authorizer checks entity, field and return-field access on the canonical semantic mutation plan.
- The Foundgine mutation authorizer does not apply read predicates to inserts. Host-side tenant stamping and composite `(tenant_id, company_id)` foreign keys therefore remain mandatory.
- Generated relationship topology is authorization-gated. The current generated graph includes `Company.Opportunities` and `Opportunity.Company`. Optional `Contact.CompanyId` remains a scalar semantic dimension rather than a graph edge because the current generator requires exact FK/PK CLR type equality (`Guid?` versus `Guid`). Each traversed target entity also carries the tenant predicate; relationship access is not a substitute for row-level isolation.
- System.Text.Json metadata is generated at compile time and reflection fallback is disabled.

## AOT status and release gates

The CRM has the Foundgine AOT metadata analyzer integration and source-generated JSON metadata. `IsAotCompatible` is enabled and Native AOT publishing is opt-in. This does **not** mean a successful Native AOT publish has been verified: this environment has no .NET SDK.

Before release:

1. `dotnet restore` and `dotnet build -warnaserror`.
2. Inspect compiler output to confirm `Foundgine.Generated.GeneratedMetadata` is emitted from the three CRM domain records; fail the build if it is absent.
3. Run verification assertions for generated entities, fields, storage names, relationships, semantic overlays, request-scope isolation and authorization.
4. Run PostgreSQL E2E tests with at least two tenants, including cross-tenant relationship/FK attempts.
5. Run `dotnet publish -c Release -r linux-x64 -p:EnableNativeAot=true -p:SelfContained=true`.
6. Start the Native AOT container and run health, Keycloak authentication, reads and mutations. Treat all trim/AOT warnings as release blockers.

## Discovery

- `GET /api/semantic/capabilities` returns the stable, schema-only CRM catalog (`crm.semantic.v1`).
- `GET /api/semantic/capability-contract` uses Foundgine's `SemanticCapabilityContractDiscovery` against the composed semantic model and the caller's current authorization policy. Tenant predicates are represented as conditional access; executable predicates are not exposed to clients. The transport projection also omits the host-controlled `TenantId` field.

Neither endpoint grants access. Execution always re-authorizes each operation.
