# ERP Foundgine model (increments 1–2)

`erp/src/main/java/com/enterprise/erp/foundgine/` contains the fluent semantic model and its first JPA execution/authorization integration.

| File | Role |
|---|---|
| `ErpFoundgineModel` | single registration point; `build()` returns the `MetadataRegistry` |
| `ErpSemantics` | entity names, aliases, dimensions, relationships |
| `ErpStorage` | tables, columns, lengths, precision, indexes, checks (mirrors the Flyway DDL) |
| `ErpEntityMaps` | semantic member to persistence member (the JPA classes are both) |
| `ErpConnections` | foreign-key joins between related entities |
| `ErpOrm` | configures the six mapped entities and the Foundgine JPA provider |
| `ErpAuthorization` | binds the validated JWT caller to read, field, relationship and tenant authorization |

Modelled: customer, sales order, return, shipment, invoice, payment.
Not modelled: `erp_products`, `erp_inventory`, `sales_order_lines`, `stock_reservations`, `erp_return_lines`. Foundgine
entities have a single key column; these tables have composite keys, so they stay on the ERP's own JPA repositories.

Flyway still owns the schema. `ErpStorage` mirrors it by hand; do not generate a second migration from it.

## Authorization policy

- The policy is read-only: all non-`READ` operations are denied.
- Customer and sales-order reads are available to sales, finance and admin roles.
- Return and shipment reads are available to sales, inventory and admin roles.
- Invoice and payment reads are available to finance and admin roles.
- `CorrelationId` and `IdempotencyKey` are admin-only fields.
- Relationship traversal is allowed only when the caller can read the target entity.
- Every read receives a tenant predicate (`resource.tenantId == context.tenantId`). The tenant is taken from the validated JWT through `ErpCaller.requireTenant()`; missing tenant claims fail closed.

The JPA typed repository remains a mechanical persistence API, not an authorization boundary. Application services continue to own writes and transactions.

## Verification status

- Increment 1: compiled with `javac` 25 against the real `foundgine-core` sources (stub JPA annotations) and built the registry: 6 entities, 12 relationships.
- Increment 2: the supplied checkpoint reports successful compilation of the provider ORM sources against temporary Jakarta API stubs and successful compilation of the ERP authorization/configuration classes against the real core/provider source outputs.
- The checkpoint authorization harness passed: six-entity ORM configuration; sales order allow; invoice denial for sales; invoice allow for finance; admin-only internal field; missing-tenant rejection in both planning and execution context.
- `ErpAuthorizationTest` captures the complete six-entity read-role matrix (sales, finance, inventory, admin and enterprise-admin), both admin-only integration fields, and missing-tenant rejection. It has not yet been executed in this environment.
- `ErpTenantIsolationJpaTest` now exercises `FoundgineJpaReader` against Hibernate/JPA and an isolated H2 database. It checks normal tenant scoping, a contradictory caller-supplied tenant filter, a matching filter, and missing-tenant rejection. It is a real-provider integration test definition, but has not yet been run here.
- Provider source inspection on the public Foundgine `main` branch confirms the intended execution path: `FoundgineJpaReader` obtains the caller execution context; `OrmExecutionProvider` carries it through every plan node; `OrmQueries` lowers the authorization predicate with that context and appends it as a separate `where` clause before caller filters; `JpaOrmQuerySource` translates the combined clauses to JPA Criteria predicates and binds their values. This source inspection is not a substitute for executing the test against the exact 3.3.5 artifact.

**Not yet certified:** Maven compilation and execution against the exact Foundgine 3.3.5 artifacts, Hibernate/H2 integration test results, PostgreSQL-specific behavior, and a full Spring application-context test. The JPA provider remains JVM/reflection mode; Native Image requires generated `JpaEntityModel` registrations.

## Remaining increments

Foundgine MCP in place of the hand-written `/mcp`, `foundgine-spring` auto-configuration, Keycloak and Temporal extensions, GraphQL, outbox/event store, warrants/authorization policy hardening, and full end-to-end integration tests.
