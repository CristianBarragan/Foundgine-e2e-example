# MCP servers (checkpoint 18, increment 6)

Both services expose a **read-only MCP server** (Streamable HTTP, `POST /mcp`, no sessions, no SSE). Agents read business
facts through MCP; every write stays behind the REST APIs and the Temporal workflows.

| | CRM (`Crm.Api`, .NET) | ERP (`erp`, Java) |
|---|---|---|
| Implementation | **Foundgine MCP adapter** `FoundgineMcpTools` (Foundgine.Providers 3.3.3) + `ModelContextProtocol.AspNetCore` | Hand-written JSON-RPC core (`com.enterprise.erp.mcp`) over `ErpFacts`; the ERP now depends on Foundgine Java 3.3.5 (`foundgine-core` + `foundgine-providers`) for its semantic/JPA layer, but its native MCP adapter is not yet wired |
| Tools | Foundgine capability discovery + read-intent execution over the CRM semantic model | `check_sku`, `check_skus` (same output as the LangChain4j bot's tool) |
| Switch | `Mcp__Enabled=true` | `ERP_MCP_ENABLED=true` (`mcp.enabled`) |
| Authentication | Keycloak bearer (same as REST) | Keycloak bearer (same as REST) |
| Authorization | `crm.read` policy (`enterprise-admin`, `crm-admin`, `crm-sales`, `crm-support`) | `erp-inventory`, `erp-admin`, `enterprise-admin` |
| Tenant / caller | token -> Keycloak provider -> `ISecurityExecutionContextProvider` (warrant); Foundgine fails closed without one | token -> `ErpCaller`; a token without tenant gets 403 |
| Writes | **Not exposed**: `FoundgineMcpMutationTools` is deliberately not registered | none exist |

## Rules both sides follow
- A tenant, user or role is never an MCP argument; the model cannot choose whose data it reads (the ERP test sends a
  `tenantId` argument and asserts it is ignored).
- Discovery grants nothing: Foundgine (CRM) authorizes at execution time; the ERP authorizes before the handler runs.
- ERP: a browser `Origin` header is rejected unless listed in `mcp.allowed-origins` (DNS-rebinding guard from the transport spec).
  CRM: the SDK's stateless HTTP transport; the endpoint is behind the same authentication and rate limiter as the REST API.
- ERP protocol coverage: legacy/handshake MCP (`initialize`, `notifications/initialized`, `ping`, `tools/list`, `tools/call`),
  negotiating `2025-11-25`, `2025-06-18`, `2025-03-26` and `2024-11-05`. The `2026-07-28` stateless/modern protocol is
  **not implemented**; a modern version requested through legacy `initialize` is counter-offered as `2025-11-25`. Notifications get 202;
  batches, GET and DELETE are refused (405 / invalid request). JSON-RPC envelopes now
  require `jsonrpc: "2.0"`, a non-empty method, string/integer request IDs, and object-valued `params` when present.
  `tools/call` also validates the tool name and object-shaped arguments before dispatch; missing arguments become an empty object,
  tool input failures are returned as MCP tool errors, and unexpected runtime failures are redacted rather than exposing provider details.

## Try it
    # CRM
    Mcp__Enabled=true dotnet run --project crm/src/Crm.Api
    # ERP
    ERP_MCP_ENABLED=true mvn -f erp/pom.xml spring-boot:run
    curl -s localhost:8082/mcp -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
      -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"check_sku","arguments":{"sku":"SKU-1"}}}'

## Not done yet (next increments)
- The bots do not consume MCP yet: C# sales bot (Microsoft Agent Framework) -> ERP `/mcp`; Java fulfilment bot (`langchain4j-mcp`) -> CRM `/mcp`.
  Both need a token that carries a tenant, so a service-account path with tenant is the open question (workflow input has the tenant;
  tokens and secrets must not enter workflow history).
- No `erp-sales` role exists, so CRM sales users cannot call the ERP `/mcp` yet.
- Verify the exact Foundgine Java artifact/version and MCP adapter API and its security-context bridge, then replace the hand-written ERP core with the native adapter over the ERP semantic model. Do not pass tenant, subject, roles or credentials through MCP arguments. The existing Keycloak bearer-token boundary and Origin guard must remain in front of it.
- Compose/demo overlay and `e2e/run.sh` do not enable or exercise `/mcp` yet.


## HTTP transport header requirements

The `/mcp` endpoint implements the request/response subset of the legacy Streamable HTTP transport. Every POST must include an `Accept` header explicitly listing both `application/json` and `text/event-stream`, even though this server currently returns JSON responses only. Requests other than `initialize` must include `MCP-Protocol-Version` with one of the supported legacy protocol versions (`2025-11-25`, `2025-06-18`, `2025-03-26`, or `2024-11-05`). `initialize` negotiates the version in the JSON-RPC body and does not require that header. Missing or unsupported headers are rejected at the HTTP layer before tool dispatch.

The server intentionally does not implement SSE responses, server-initiated streams, sessions, or the stateless `2026-07-28` protocol. GET returns 405. This is a documented transport subset, not a claim of complete MCP Streamable HTTP conformance. Authentication, tenant derivation from the Keycloak token, and Origin validation remain enforced by the host controller.
