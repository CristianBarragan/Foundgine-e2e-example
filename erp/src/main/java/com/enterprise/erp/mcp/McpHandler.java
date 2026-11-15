package com.enterprise.erp.mcp;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;

/**
 * Minimal MCP server core (JSON-RPC 2.0, Streamable HTTP, request/response only, no sessions, no SSE): initialize, ping,
 * tools/list and tools/call. Transport-free and Spring-free so it is unit-testable. Security is the host's job: the
 * {@link Caller} is built from the validated token by the controller; tool arguments can never name a tenant.
 */
public final class McpHandler {
    /** Latest supported legacy/handshake protocol revision; the 2026 stateless era is not implemented here. */
    public static final String LATEST = "2025-11-25";
    static final List<String> SUPPORTED = List.of(LATEST, "2025-06-18", "2025-03-26", "2024-11-05");
    static final int PARSE_ERROR = -32700, INVALID_REQUEST = -32600, METHOD_NOT_FOUND = -32601, INVALID_PARAMS = -32602;
    private static final ObjectMapper JSON = new ObjectMapper();

    /** Trusted identity of the call, from the token only. */
    public record Caller(String tenantId, String subject) {}

    /** Raised by a tool for a bad argument; reported to the model as a tool error, never as a transport failure. */
    public static final class ToolInputException extends RuntimeException {
        public ToolInputException(String message) { super(message); }
    }

    public interface Tool {
        String name();
        String description();
        /** JSON Schema of the arguments. */
        ObjectNode inputSchema();
        /** Text result. Runs only after the caller's tenant was established. */
        String call(Caller caller, JsonNode arguments);
    }

    private final Map<String, Tool> tools = new LinkedHashMap<>();
    private final String serverName;
    private final String serverVersion;

    public McpHandler(String serverName, String serverVersion, List<? extends Tool> tools) {
        this.serverName = serverName;
        this.serverVersion = serverVersion;
        for (var t : tools) this.tools.put(t.name(), t);
    }

    /** Handles one HTTP POST body. Empty result means a notification or response: answer 202 with no body. */
    public Optional<ObjectNode> handle(String body, Caller caller) {
        JsonNode request;
        try {
            request = JSON.readTree(body == null ? "" : body);
        } catch (Exception e) {
            return Optional.of(error(null, PARSE_ERROR, "Parse error"));
        }
        if (request == null || !request.isObject()) return Optional.of(error(null, INVALID_REQUEST, "Batches and non-objects are not supported"));
        var id = request.get("id");
        if (!"2.0".equals(request.path("jsonrpc").asText())) {
            return Optional.of(error(validErrorId(id), INVALID_REQUEST, "jsonrpc must be 2.0"));
        }
        var methodNode = request.get("method");
        if (methodNode == null || !methodNode.isTextual() || methodNode.asText().isBlank()) {
            // A JSON-RPC response sent by the client (no method) is accepted and ignored.
            if (!request.has("method") && (request.has("result") || request.has("error"))) return Optional.empty();
            return Optional.of(error(validErrorId(id), INVALID_REQUEST, "method must be a non-empty string"));
        }
        if (id != null && !id.isTextual() && !id.isIntegralNumber()) {
            return Optional.of(error(null, INVALID_REQUEST, "id must be a string or integer"));
        }
        if (id == null) return Optional.empty(); // notification (e.g. notifications/initialized)
        var method = methodNode.asText();
        var params = request.path("params");
        if (!params.isMissingNode() && !params.isObject()) {
            return Optional.of(error(id, INVALID_PARAMS, "params must be an object"));
        }
        return Optional.of(switch (method) {
            case "initialize" -> result(id, initialize(params));
            case "ping" -> result(id, JSON.createObjectNode());
            case "tools/list" -> result(id, listTools());
            case "tools/call" -> callTool(id, params, caller);
            default -> error(id, METHOD_NOT_FOUND, "Method not found: " + method);
        });
    }

    private ObjectNode initialize(JsonNode params) {
        var requested = params.path("protocolVersion").asText("");
        var version = SUPPORTED.contains(requested) ? requested : LATEST;
        var out = JSON.createObjectNode();
        out.put("protocolVersion", version);
        out.putObject("capabilities").putObject("tools").put("listChanged", false);
        out.putObject("serverInfo").put("name", serverName).put("version", serverVersion);
        return out;
    }

    private ObjectNode listTools() {
        var out = JSON.createObjectNode();
        var array = out.putArray("tools");
        for (var t : tools.values()) {
            var n = array.addObject();
            n.put("name", t.name());
            n.put("description", t.description());
            n.set("inputSchema", t.inputSchema());
            n.putObject("annotations").put("readOnlyHint", true).put("openWorldHint", false);
        }
        return out;
    }

    private ObjectNode callTool(JsonNode id, JsonNode params, Caller caller) {
        var name = params.path("name");
        if (!name.isTextual() || name.asText().isBlank()) return error(id, INVALID_PARAMS, "tools/call requires a non-empty tool name");
        var tool = tools.get(name.asText());
        if (tool == null) return error(id, INVALID_PARAMS, "Unknown tool: " + name.asText());
        var arguments = params.path("arguments");
        if (arguments.isMissingNode()) arguments = JSON.createObjectNode();
        if (!arguments.isObject()) return error(id, INVALID_PARAMS, "tool arguments must be an object");
        var content = JSON.createObjectNode();
        try {
            content.put("isError", false);
            content.putArray("content").addObject().put("type", "text").put("text", tool.call(caller, arguments));
        } catch (ToolInputException e) {
            content.put("isError", true);
            content.putArray("content").addObject().put("type", "text").put("text", e.getMessage());
        } catch (RuntimeException e) {
            // Do not leak database, provider, or implementation details to an untrusted MCP caller.
            content.put("isError", true);
            content.putArray("content").addObject().put("type", "text").put("text", "Tool execution failed");
        }
        return result(id, content);
    }

    private static JsonNode validErrorId(JsonNode id) {
        return id != null && (id.isTextual() || id.isIntegralNumber()) ? id : null;
    }

    private static ObjectNode result(JsonNode id, JsonNode result) {
        var out = envelope(id);
        out.set("result", result);
        return out;
    }

    static ObjectNode error(JsonNode id, int code, String message) {
        var out = envelope(id);
        out.putObject("error").put("code", code).put("message", message);
        return out;
    }

    private static ObjectNode envelope(JsonNode id) {
        var out = JSON.createObjectNode();
        out.put("jsonrpc", "2.0");
        if (id == null) out.putNull("id"); else out.set("id", id);
        return out;
    }
}
