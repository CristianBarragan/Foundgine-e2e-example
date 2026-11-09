# Building the Java ERP bounded context

The project is now standalone and no longer requires the missing Foundgine monorepo parent POM.
It targets Java 21 and pins the dependency versions derived from the supplied flattened POM.

From this directory:

```bash
mvn clean verify
```

The first build requires Maven Central access for the pinned Foundgine 2.3.1 artifacts and other
Maven dependencies. This repository packaging change has not yet been build-verified because Maven
is not installed in the authoring environment. A passing `mvn clean verify` is therefore still an
acceptance gate, not a claimed result.

## Integration endpoints (Temporal saga)

```bash
FOUNDGINE_POSTGRES_CONNECTION_STRING=... ERP_JWT_ISSUER=... ERP_JWT_AUDIENCE=foundgine-erp \
ERP_JWKS_URI=https://identity.example/realms/foundgine/protocol/openid-connect/certs \
java -cp target/classes:<deps> com.foundgine.samples.supplychain.advanced.integration.IntegrationServer
```
Set `ERP_APPLY_SCHEMA=true` for dev only (applies schema, `integration.sql` and demo seed). Default port 8082.
