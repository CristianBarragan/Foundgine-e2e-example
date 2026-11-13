# MCP servers (checkpoint 18, increment 3)

Both services expose a **read-only MCP server** (Streamable HTTP, `POST /mcp`, no sessions, no SSE). Agents read business
facts through MCP; every write stays behind the REST APIs and the Temporal workflows.

| | CRM (`Crm.Api`, .NET) | ERP (`erp`, Java) |
|---|---|---|
| Implementation | **Foundgine MCP adapter** `FoundgineMcpTools` (Foundgine.Providers 3.3.3) + `ModelContextProtocol.AspNetCore` | Hand-written MCP core (`com.enterprise.erp.mcp`) over `ErpFacts` (no Foundgine Java artifact in this repo, decision 4) |
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
- ERP protocol coverage: `initialize` (version negotiation 2025-06-18 / 2025-03-26 / 2024-11-05), `ping`, `tools/list`,
  `tools/call`; notifications get 202; batches, GET and DELETE are refused (405 / invalid request).

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
- When a Foundgine Java artifact is available, replace the hand-written ERP core with its MCP adapter over the ERP semantic model.
- Compose/demo overlay and `e2e/run.sh` do not enable or exercise `/mcp` yet.
