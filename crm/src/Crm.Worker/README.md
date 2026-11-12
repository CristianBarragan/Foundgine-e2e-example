# Crm.Worker

Dedicated .NET 10 worker process hosting the Foundgine process worker and the C# customer-to-cash Temporal workflow worker. It intentionally runs separately from `Crm.Api` because
`AddFoundgineTemporalWorker` installs an activity-scoped `ISecurityExecutionContextProvider`; sharing that
container with HTTP request authentication would mix two different authority lifecycles.

## Composition

- `CustomerToCashWorkflow` polls the dedicated `crm-orchestration` queue. It coordinates approval and invokes the Java `QuoteToOrderWorkflow` child on the separate `orchestration` queue. These queues must remain separate: Temporal does not route workflow tasks by workflow type.

- `Foundgine.Extensions.Temporal`: Temporal worker hosting, registered process definitions, operation execution,
  retries, compensation, approval/lifecycle handling.
- `Foundgine.Extensions.Keycloak`: worker warrant issuance and activity security context.
- `Foundgine.Extensions.Keycloak.Admin`: registered Keycloak administration operations used by
  `TenantUserProvisioning`.
- `Foundgine.Providers`: used transitively by the Extensions integration for provider contracts/outbox seams.
  No storage provider is registered here because this worker does not execute CRM persistence operations; those
  remain on the CRM application/activity host.
- Additional providers (EF Core, SQL, PostgreSQL, Redis, AI, MCP, GraphQL, etc.) are not blindly loaded into this
  process. Register one only when a worker operation actually invokes that capability and configure the same
  semantic model/authorization boundary as the owning application. This avoids creating a second, divergent
  data/runtime authority in a process worker.

## Configuration

Set `Foundgine__Temporal__Address`, `Foundgine__Temporal__Namespace`, and
`Foundgine__Temporal__TaskQueue`. Set `Foundgine__Temporal__CustomerToCashTaskQueue` to `crm-orchestration` (default). Supply a persistent worker-specific warrant signing key in production through
`Foundgine__Keycloak__WarrantPrivateKeyPemPath`, and configure the verifier(s) with its public key.
`EphemeralWarrantKey=true` is Development-only. Supply the Keycloak service-account secret using
`Foundgine__Keycloak__ServiceClientSecret` or a secret provider; do not commit it.

The workflow input contains tenant and initiator identifiers only. Tokens, client secrets and warrants must never
be added to Temporal workflow input, activity payloads, logs, or workflow history. Worker roles are least-privilege
and every activity attempt obtains a fresh tenant-bounded warrant.

## Run

```sh
dotnet run --project crm/src/Crm.Worker/Crm.Worker.csproj
```

The process uses `RunAsync` so both Temporal hosted services receive graceful host shutdown.
