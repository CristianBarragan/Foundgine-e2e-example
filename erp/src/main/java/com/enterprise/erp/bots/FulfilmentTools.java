package com.enterprise.erp.bots;

import dev.langchain4j.agent.tool.P;
import dev.langchain4j.agent.tool.Tool;
import java.util.List;

/**
 * Read-only ERP tools offered to the LangChain4j fulfilment bot. One instance per turn, bound to the tenant of the workflow
 * input: the model supplies a SKU, never a tenant. No Foundgine Java artifact is in this repo, so these tools sit directly on
 * {@link ErpFacts}; swap the {@code ErpFacts} bean to change the source. The ERP's MCP server (com.enterprise.erp.mcp) serves
 * the same {@link #checkSku} output to MCP clients.
 */
public class FulfilmentTools {
    private final String tenantId;
    private final ErpFacts facts;

    public FulfilmentTools(String tenantId, ErpFacts facts) {
        this.tenantId = tenantId;
        this.facts = facts;
    }

    @Tool("Look up one SKU in the ERP: whether it is active, how many units are available to promise, and the minimum "
            + "unit price the business will accept. Returns a short JSON object, or says the SKU is unknown.")
    public String checkSku(@P("The SKU code, e.g. SKU-1") String sku) {
        var f = facts.factsFor(tenantId, List.of(sku)).get(sku);
        if (f == null) return "{\"sku\":\"" + escape(sku) + "\",\"known\":false}";
        return "{\"sku\":\"" + escape(f.sku()) + "\",\"known\":true,\"active\":" + f.active()
                + ",\"available\":" + f.available() + ",\"minUnitPrice\":" + f.minUnitPrice().toPlainString() + "}";
    }

    private static String escape(String s) { return s.replace("\\", "\\\\").replace("\"", "\\\""); }
}
