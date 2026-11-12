# Keycloak (Phase 2)

Realm `enterprise` is imported from `infrastructure/keycloak/enterprise-realm.json` on `./start.sh` (dev only).

| Client | Type | Notes |
|---|---|---|
| `crm-api`, `erp-api` | bearer-only resources | token audiences |
| `enterprise-web` | public, PKCE, password grant (dev) | audiences crm-api + erp-api |
| `crm-worker` | service account | audience crm-api only, role `integration-service` |
| `erp-worker` | service account | audience erp-api only, role `integration-service` |

Roles: enterprise-admin, crm-admin, crm-sales, crm-support, erp-admin, erp-sales, erp-inventory, erp-finance, integration-service.
Claims in access tokens: `sub`, `roles` (realm roles), `tenant_id` (user attribute), `aud`.
Users (password `dev-password`): alice crm-sales/acme, bob crm-support/acme, carol crm-admin/globex, dave enterprise-admin/acme.

Try it:

    curl -s -d grant_type=password -d client_id=enterprise-web -d username=alice -d password=dev-password \
      http://localhost:8080/realms/enterprise/protocol/openid-connect/token | jq -r .access_token
    curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/v1/whoami

    curl -s -d grant_type=client_credentials -d client_id=crm-worker -d client_secret=crm-worker-dev-secret \
      http://localhost:8080/realms/enterprise/protocol/openid-connect/token

Open for later phases: service accounts carry no `tenant_id`, so they cannot call tenant-scoped CRM endpoints yet. Phase 6 must
define how a worker acts for a tenant (e.g. tenant passed in the workflow input and a dedicated integration policy) without
tokens in Temporal history. ERP-side validation (Java) is deferred with the ERP.

## Foundgine Keycloak + Temporal providers (CRM)

The CRM no longer hand-rolls JWT validation. `Foundgine.Extensions` 3.3.3 supplies both providers (guide: `Keycloak/README.md`,
`Temporal/README.md` in the Foundgine source).

**API (`Crm.Api`)**
- `AddCrmIdentity()` -> `AddFoundgineKeycloak`: issuer/audience/signature/lifetime validation, `realm_access.roles` projected to
  ASP.NET roles (the realm now emits that claim via the `realm-access-roles` mapper; the flat `roles` claim is kept for other consumers),
  tenant from `tenant_id`, and `KeycloakSecurityExecutionContextProvider` (a fresh single-use signed warrant per call).
  Config: `Foundgine:Keycloak` (`RoleGrants` map CRM roles to capabilities; at least one is mandatory).
- Warrant signing key: mounted PEM (`WarrantPrivateKeyPemPath`). `EphemeralWarrantKey` is Development-only and the host refuses it elsewhere.
- Temporal hooks (when `Temporal:Enabled`): `AddFoundgineTemporal` (client, process catalog, starter, provider descriptor),
  `MapFoundgineTemporal` under `/api/v1/workflows` (admin-only + audited; tenant/subject come from the warrant context, so another tenant's
  workflow cannot be addressed), and `OutboxRouter`.
- `OutboxRouter` is the seam between the CRM outbox and Temporal: topics in `Foundgine:Temporal:TopicRoutes` go through
  `FoundgineTemporalOutboxPublisher` (provider hook); the three existing types keep starting the Java workflows. Both carry the
  Keycloak subject (`outbox_messages.initiator`, migration `20261113000200`) as workflow memo `initiator` / message header, plus `tenantId`.
  No token or warrant is stored in Temporal history.

**Worker (`Crm.Worker`, queue `foundgine`)**: `AddFoundgineKeycloakWarrants().UseForTemporalWorker()` + `AddFoundgineTemporalWorker`
+ `AddKeycloakAdminOperations()`. Activities act with `WorkerRoles` (`integration-service`) bounded to the workflow's tenant, initiator
as audit subject. This closes the "how a worker acts for a tenant" gap above for Foundgine processes. Process `tenant-user-provisioning`
(Keycloak group + user, with compensation): `POST /api/v1/workflows/tenant-user-provisioning` `{ "businessKey": "...", "payload": {...} }`.
The worker's service account (`crm-worker`) now has the `realm-management` roles needed for the Admin API.

**Not done / caveats**
- Nothing here was compiled or run (no .NET SDK, no Maven Central, no Keycloak in the build environment).
- The runtime's warrant verification (`HighAssurance`) is not enabled: CRM services still authorize in the services (decision 8);
  warrants are issued but only consumed by Foundgine gateway operations.
- No topic is routed to a Foundgine process by default; the Java workflows are unchanged. ERP JWT validation is still open.
- `tenant_id` is a Keycloak unmanaged attribute (`ENABLED`); the guide says to declare it in the User Profile, admin-only. Do that before real use.
- Foundgine starts use their own lazily-connected client: no OpenTelemetry `TracingInterceptor` on it yet.
