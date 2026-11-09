# Foundgine 2.3.1 upgrade notes

## Done
- Foundgine NuGet packages (Core, Runtime, Providers, Extensions) in the CRM API: 2.3.1.
- ERP Maven `foundgine.version`: 2.3.1.
- All projects moved from .NET 9 to .NET 10 (csproj, Dockerfiles, dotnet-ef tool 10.0.0);
  ASP.NET, Hosting, Http, EF Core and Npgsql packages bumped to 10.x.
- README / COMPATIBILITY version mentions updated.

## Refactor 1 done: CRM login -> `AddFoundgineKeycloak` (uncompiled, untested)
- Removed `Foundgine/CrmMcpSecurity.cs` (`CrmWarrantAuthority`, `CrmHttpSecurityExecutionContextProvider`).
- `AuthSetup.cs`: `AddCrmKeycloak` wires JWT validation, role projection, warrant issuer, key resolver and
  `ISecurityExecutionContextProvider` from `Foundgine.Extensions`; `AddCrmAuthorizationPolicies` keeps the four policies.
- New `Foundgine/CrmKeycloak.cs`: role -> grant mapping and a `ConstraintsFactory` keeping per-role fields,
  tenant pinning, read-only operation and the 200-row cap. Issuer, audience, key id, 2-minute lifetime unchanged.
- Existing `Keycloak:*` config and docker-compose env vars reused; `Keycloak:ValidIssuer` kept via `PostConfigure<JwtBearerOptions>`.
  The realm already emits `tenant_id` and the `crm-api` audience.
- `CrmMcpSecurityTests.cs` rewritten against the issuer (plus unmapped-role and fresh-warrant checks).

Behaviour differences to review:
1. Users with no CRM role used to get read access if they had a tenant; now no Foundgine context (deny by default).
2. Inbound claim mapping is off (`sub` stays `sub`); `CrmScope` and the rate limiter already read `sub`/`tenant_id`.
3. Realm roles only (client roles disabled), as before.
4. Signing key is per-process unless `Keycloak:WarrantPrivateKeyPemPath` is set; set it (or use KMS) before multi-replica.
5. `WarrantReplayStore` is still in-memory; multi-replica needs a shared store.
6. `AddFoundgineKeycloak` also registers `FoundgineKeycloakProvider` in the provider registry; unverified next to the CRM's SQL provider.
Upstream documents the extension as not verified against a live Keycloak.

## Not done (needs decisions; behaviour-changing)
Foundgine.Extensions 2.3.1 adds a Temporal integration.
- Saga (outbox dispatcher, orchestration worker/API) -> Temporal extension; the saga must be
  rewritten as ordered steps with compensation.
- The fluent `FoundgineModel` API is optional; attributes still work.

## Caveats
- Nothing was restored, built or tested (no NuGet / Maven / SDK access). Run `dotnet restore && dotnet build`.
- Upstream also has a 2.3.2 tag; 2.3.1 was targeted as requested.
- The stale `obj/` from a .NET 9 build was removed from this zip.
