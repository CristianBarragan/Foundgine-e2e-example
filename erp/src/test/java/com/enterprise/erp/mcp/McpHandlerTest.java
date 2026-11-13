package com.enterprise.erp.mcp;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.enterprise.erp.bots.ErpFacts;
import com.enterprise.erp.bots.StockFact;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** Protocol behaviour and tenant binding of the ERP MCP core; no Spring, no database. */
class McpHandlerTest {
    static final ObjectMapper JSON = new ObjectMapper();
    final List<String> askedTenants = new ArrayList<>();
    final ErpFacts facts = (tenant, skus) -> {
        askedTenants.add(tenant);
        return Map.of("SKU-1", new StockFact("SKU-1", true, 7, new BigDecimal("90.00")));
    };
    final McpHandler handler = new McpHandler("erp-mcp", "1", ErpMcpTools.over(facts));
    final McpHandler.Caller acme = new McpHandler.Caller("acme", "u1");

    JsonNode call(String body) throws Exception { return JSON.readTree(handler.handle(body, acme).orElseThrow().toString()); }

    @Test void initializeNegotiatesTheVersionAndAdvertisesTools() throws Exception {
        var r = call("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-03-26\"}}");
        assertEquals("2025-03-26", r.at("/result/protocolVersion").asText());
        assertTrue(r.at("/result/capabilities/tools").isObject());
        assertEquals("2.0", r.get("jsonrpc").asText());
        assertEquals(1, r.get("id").asInt());
        var unknown = call("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"1999-01-01\"}}");
        assertEquals(McpHandler.LATEST, unknown.at("/result/protocolVersion").asText());
    }

    @Test void notificationsGetNoBody() {
        assertTrue(handler.handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", acme).isEmpty());
    }

    @Test void listsOnlyReadOnlyTools() throws Exception {
        var tools = call("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}").at("/result/tools");
        assertEquals(2, tools.size());
        for (var t : tools) assertTrue(t.at("/annotations/readOnlyHint").asBoolean(), t.get("name").asText());
    }

    @Test void checkSkuAnswersForTheCallersTenantAndIgnoresATenantArgument() throws Exception {
        var r = call("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"check_sku\","
                + "\"arguments\":{\"sku\":\"SKU-1\",\"tenantId\":\"other\"}}}");
        assertFalse(r.at("/result/isError").asBoolean());
        assertTrue(r.at("/result/content/0/text").asText().contains("\"available\":7"));
        assertEquals(List.of("acme"), askedTenants);
    }

    @Test void badArgumentsAreToolErrorsNotTransportErrors() throws Exception {
        var r = call("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"check_sku\",\"arguments\":{}}}");
        assertTrue(r.at("/result/isError").asBoolean());
        var many = new StringBuilder();
        for (int i = 0; i <= ErpMcpTools.MAX_SKUS; i++) many.append(i == 0 ? "" : ",").append("\"S").append(i).append('"');
        var r2 = call("{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/call\",\"params\":{\"name\":\"check_skus\",\"arguments\":{\"skus\":[" + many + "]}}}");
        assertTrue(r2.at("/result/isError").asBoolean());
        assertTrue(askedTenants.isEmpty());
    }

    @Test void protocolErrors() throws Exception {
        assertEquals(McpHandler.METHOD_NOT_FOUND, call("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"resources/list\"}").at("/error/code").asInt());
        assertEquals(McpHandler.INVALID_PARAMS, call("{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"tools/call\",\"params\":{\"name\":\"nope\"}}").at("/error/code").asInt());
        assertEquals(McpHandler.PARSE_ERROR, call("{not json").at("/error/code").asInt());
        assertEquals(McpHandler.INVALID_REQUEST, call("[{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}]").at("/error/code").asInt());
    }
}
