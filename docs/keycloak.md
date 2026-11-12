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
