# Checkpoint 9 — CRM / ERP process-worker integration

## Baseline and scope

Checkpoint 8 is the platform baseline. `src.zip` is the Foundgine source/API reference for the .NET integrations.
`Crm.Api` is deliberately unchanged. Secret rotation remains the next checkpoint after the worker integrations.

This increment keeps the ownership boundaries explicit:

| Runtime | Owns |
|---|---|
| `Crm.Api` | HTTP authentication, CRM domain/storage, outbox publishing, CRM Temporal activity queue |
| `Crm.Worker` (.NET) | Foundgine process definitions/execution, Keycloak admin process operations, worker-scoped warrant authority |
| Temporal orchestration worker (Java) | Durable workflow state, retries, timers, compensation and workflow signals |
| ERP Spring service / Java worker | ERP customer, order/reservation and returns activities; ERP persistence and business validation |

## Foundgine .NET integrations used

`Crm.Worker` uses the actual C# package `Foundgine.Extensions` (3.3.3 in this baseline), not a hand-written Temporal
client or direct Keycloak REST adapter:

- **Temporal extension/provider** — `AddFoundgineTemporalWorker`, `AddProcess`, process catalog and operation
  registration, activity execution, compensation/approval lifecycle and Temporal hosted-service shutdown.
- **Keycloak extension/provider** — `AddFoundgineKeycloakWarrants(...).UseForTemporalWorker()` to create
  activity-scoped, tenant-bounded Foundgine security contexts.
- **Keycloak Admin extension operations** — `AddKeycloakAdminOperations()` for the process's group/user lifecycle.
- **Foundgine.Providers contracts** — consumed transitively by the extension integration for provider and outbox
  seams. The worker does not register a second storage provider because it does not own CRM persistence.

Other providers/extensions remain active at their owning boundary rather than being loaded indiscriminately:
the CRM's configured persistence/provider integrations remain in `Crm.Api`; Temporal is the durable orchestration
provider; Keycloak is the identity bridge; Java ERP owns its relational persistence. GraphQL, MCP, AI, Redis, SQL,
PostgreSQL and other provider integrations should be registered in this worker only if a registered process operation
actually uses them. Doing so without a matching semantic model, authorization policy, and owning data boundary would
create a second authority and violate the existing design. This is intentional provider reuse, not a generic
"register every package" composition.

## Cross-language contract rules

- JSON is camelCase and schemas under `contracts/schemas/` are authoritative.
- Activity names are stable contract identifiers (`erp.*`, `crm.*`); Java interface method names may change without
  changing those identifiers.
- Tenant IDs and business IDs are explicit contract fields, but tenant authority is derived from authenticated
  context at the service boundary. Never accept a caller-supplied tenant as authorization proof.
- No Keycloak access token, service secret, private key or Foundgine warrant is placed in workflow/activity input.
- ERP customer creation is idempotent on tenant + CRM customer; order creation/reservation is atomic and idempotent on
  tenant + quote; return authorization/completion is idempotent on tenant + return.
- CRM activity queue remains hosted by `Crm.Api` and is not moved into `Crm.Worker`. The .NET worker executes
  Foundgine process definitions on the `foundgine` queue.

## Lifecycle and security

A separate .NET worker host is required because `AddFoundgineTemporalWorker` installs an activity-scoped
`ISecurityExecutionContextProvider`. Each activity attempt obtains fresh authority; warrants are short-lived,
single-use and tenant-constrained. Use a persistent, host-specific signing key in production and publish only its
public key to verifiers. Ephemeral keys are development-only. Use a least-privilege `WorkerRoles` grant.

Temporal owns durable process lifecycle (retries, timers, compensation and workflow history); Foundgine owns
semantic process definitions, operation registration and authorization at execution time. Keep workflow inputs small
and non-secret. Restrict access to the Temporal frontend and protect external signal/decision endpoints at their
authenticated application boundary.

## Configuration and run

The .NET worker reads `Foundgine__Temporal__*` and `Foundgine__Keycloak__*` environment variables. Supply
`Foundgine__Keycloak__ServiceClientSecret` through a secret manager. Production must set
`EphemeralWarrantKey=false` and configure `WarrantPrivateKeyPemPath` to a mounted key file.

The Java orchestration worker accepts `TEMPORAL_ADDRESS`, `TEMPORAL_NAMESPACE` and
`TEMPORAL_ORCHESTRATION_TASK_QUEUE`. The ERP worker is enabled with `temporal.enabled=true` and registers the
customer, order and returns activity implementations on the `erp` task queue.

## Explicitly deferred

- Secret rotation, key rollover and automated revocation/rotation runbooks.
- Changes to `Crm.Api`.
- New providers with no operation/use case in this process.
