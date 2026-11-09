# Enterprise identity integration

The CRM archive contains a Keycloak realm configuration and validates JWTs against the
`foundgine-crm` realm. The Java ERP sample applies its own tenant/role policy and validates
client-supplied narrowing claims, but the supplied archive does not show Keycloak JWT validation.

Therefore, shared SSO is not yet implemented.

## Required integration

1. Establish a common Keycloak realm/client strategy (or trusted realm federation).
2. Define issuer, audience, subject, tenant, roles and warehouse claim contracts.
3. Map the authenticated subject and tenant into the ERP authorization context.
4. Treat client-provided scope/warehouse claims as narrowing hints only; never permit privilege
   expansion.
5. Validate issuer, audience, signature, expiry and allowed algorithms at every service boundary.
6. Keep domain authorization in CRM and ERP even when Foundgine performs central policy checks.

Do not reuse the CRM development realm credentials in production.
