# Independent deployment

Deploy CRM, ERP and the enterprise Foundgine boundary independently. A release of one application
must not require rebuilding the other.

The CRM archive already contains its own Docker Compose, API/frontend Dockerfiles, migrations,
Keycloak configuration and Terraform material. Keep those assets with `applications/crm-dotnet`.

The ERP archive contains a PostgreSQL Compose service but no application Dockerfile or standalone
Maven parent. Before production deployment:
- make the Maven project self-contained or build it from its parent multi-module repository;
- add a reproducible Java container image and health/readiness probes;
- externalize database credentials and Keycloak/JWT configuration;
- define event-bus connectivity, retry and dead-letter policies;
- add service-level resource limits, TLS and network policy.
