package com.enterprise.erp.mcp;

import com.enterprise.erp.bots.ErpFacts;
import com.enterprise.erp.bots.FulfilmentTools;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.util.List;
import java.util.stream.Collectors;

/**
 * Read-only ERP tools over {@link ErpFacts}. Output is produced by {@link FulfilmentTools} (the same code the LangChain4j
 * bot uses), so the bot and MCP clients always see the same answer. The tenant is {@link McpHandler.Caller#tenantId()}
 * (token), never an argument. Nothing here reserves, orders or writes.
 */
public final class ErpMcpTools {
    private static final ObjectMapper JSON = new ObjectMapper();
    static final int MAX_SKUS = 20;
    static final int MAX_SKU_LENGTH = 64;

    private ErpMcpTools() {}

    public static List<McpHandler.Tool> over(ErpFacts facts) {
        return List.of(new CheckSku(facts), new CheckSkus(facts));
    }

    static String validSku(JsonNode node) {
        if (node == null || !node.isTextual() || node.asText().isBlank()) throw new McpHandler.ToolInputException("sku must be a non-empty string");
        var sku = node.asText().strip();
        if (sku.length() > MAX_SKU_LENGTH) throw new McpHandler.ToolInputException("sku is longer than " + MAX_SKU_LENGTH + " characters");
        return sku;
    }

    record CheckSku(ErpFacts facts) implements McpHandler.Tool {
        public String name() { return "check_sku"; }
        public String description() {
            return "Look up one SKU in the ERP for the caller's tenant: whether it is active, units available to promise and the "
                    + "minimum unit price the business accepts. Returns a short JSON object, or known:false for an unknown SKU.";
        }
        public ObjectNode inputSchema() {
            var s = JSON.createObjectNode().put("type", "object");
            s.putObject("properties").putObject("sku").put("type", "string").put("description", "SKU code, e.g. SKU-1");
            s.putArray("required").add("sku");
            s.put("additionalProperties", false);
            return s;
        }
        public String call(McpHandler.Caller caller, JsonNode args) {
            return new FulfilmentTools(caller.tenantId(), facts).checkSku(validSku(args.get("sku")));
        }
    }

    record CheckSkus(ErpFacts facts) implements McpHandler.Tool {
        public String name() { return "check_skus"; }
        public String description() { return "Same as check_sku for up to " + MAX_SKUS + " SKUs at once. Returns a JSON array in request order."; }
        public ObjectNode inputSchema() {
            var s = JSON.createObjectNode().put("type", "object");
            var items = s.putObject("properties").putObject("skus").put("type", "array").put("maxItems", MAX_SKUS);
            items.putObject("items").put("type", "string");
            s.putArray("required").add("skus");
            s.put("additionalProperties", false);
            return s;
        }
        public String call(McpHandler.Caller caller, JsonNode args) {
            var node = args.get("skus");
            if (node == null || !node.isArray() || node.isEmpty()) throw new McpHandler.ToolInputException("skus must be a non-empty array");
            if (node.size() > MAX_SKUS) throw new McpHandler.ToolInputException("at most " + MAX_SKUS + " SKUs per call");
            var tools = new FulfilmentTools(caller.tenantId(), facts);
            var out = new java.util.ArrayList<String>();
            for (var n : node) out.add(tools.checkSku(validSku(n)));
            return out.stream().collect(Collectors.joining(",", "[", "]"));
        }
    }
}
