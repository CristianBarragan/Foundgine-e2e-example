# Domain adapter specifications

## CRM adapter

The adapter translates enterprise semantic capabilities into the CRM's supported REST/MCP
surface. It must preserve CRM tenant scoping and use the authenticated principal; tenant identity
must never be accepted from an untrusted request body.

Existing documented CRM HTTP routes include:
- `GET/POST /api/companies`
- `GET/POST /api/contacts`
- `GET/POST /api/opportunities`
- `GET /api/semantic/capabilities`
- `GET /api/semantic/capability-contract`
- `POST /graphql` (limited scalar root-field query support)

The supplied CRM documentation states that GraphQL mutations are not enabled and nested
relationship selections are rejected.

## ERP adapter

The supplied ERP MCP server documents these tools:
- `foundgine_query`
- `foundgine_mutation`
- `find_top_supplier_overdue_orders`

The mutation pipeline includes `place_order` and `cancel_order`; the PostgreSQL store provides
transactional/idempotent mutation handling. The adapter must preserve the ERP's tenant/role
authorization and must not bypass the mutation pipeline by writing directly to its database.

## Cross-domain mapping

- CRM `commercialOrderId` maps to an ERP `sourceOrderReference`; it is not an ERP primary key.
- CRM customer reference maps to an ERP customer/account reference through an explicit
  reconciliation table or agreed stable external identifier.
- ERP reservation identifiers are returned to CRM as opaque external references.
- Inventory quantities, availability and reservation state are ERP-owned.
