# Foundgine compatibility matrix

| Component | Supplied version | Integration note |
|---|---:|---|
| Foundgine NuGet packages in CRM | 2.3.1 | Embedded in CRM API |
| Foundgine Maven artifacts in ERP | 2.3.1 | Embedded in ERP process |
| CRM MCP SDK packages | 2.2.0 | Transport package versions |
| CRM target framework | .NET 10 | Separate API runtime |
| ERP language baseline | Java 21 syntax/features | Maven parent supplies inherited build settings |

Required before production integration:
- Pin compatible .NET and Java semantic contract versions.
- Add contract compatibility tests for capability discovery and mutation outcomes.
- Define error taxonomy and pagination semantics across transports.
- Define tenant and actor claim mapping from Keycloak to the ERP execution context.
- Verify idempotency and replay behavior across the saga boundary.
