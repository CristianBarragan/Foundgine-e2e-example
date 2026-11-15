package com.enterprise.erp.mcp;

import com.enterprise.erp.bots.ErpFacts;
import com.enterprise.erp.security.ErpCaller;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.util.Arrays;
import java.util.List;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.server.ResponseStatusException;

/**
 * The ERP's MCP server (Streamable HTTP, POST only) at {@code /mcp}, opt-in with {@code mcp.enabled=true}. Same Keycloak
 * bearer authentication as the REST API (SecurityConfig: everything but health needs a token). Roles: the ERP readers; tenant
 * and caller come from the token through {@link ErpCaller}, so a tool can only see the caller's own tenant and a service
 * account without a tenant gets 403. A browser {@code Origin} is rejected unless listed in {@code mcp.allowed-origins}
 * (DNS-rebinding guard from the MCP transport spec); there is no SSE stream and no session.
 */
@RestController
@RequestMapping("/mcp")
@ConditionalOnProperty(name = "mcp.enabled", havingValue = "true")
class McpController {
    private final McpHandler handler;
    private final List<String> allowedOrigins;
    private static final ObjectMapper JSON = new ObjectMapper();
    private static final String PROTOCOL_HEADER = "MCP-Protocol-Version";

    McpController(ErpFacts facts, @Value("${mcp.allowed-origins:}") String allowedOrigins) {
        this.handler = new McpHandler("erp-mcp", "1.0.0", ErpMcpTools.over(facts));
        this.allowedOrigins = Arrays.stream(allowedOrigins.split(",")).map(String::strip).filter(s -> !s.isEmpty()).toList();
    }

    @PostMapping(consumes = MediaType.APPLICATION_JSON_VALUE)
    @PreAuthorize("hasAnyRole('erp-inventory', 'erp-admin', 'enterprise-admin')")
    ResponseEntity<String> post(@RequestBody String body, @RequestHeader(value = "Origin", required = false) String origin,
                                @RequestHeader(value = "Accept", required = false) String accept,
                                @RequestHeader(value = PROTOCOL_HEADER, required = false) String protocolVersion,
                                Authentication authentication) {
        if (!acceptsBothRepresentations(accept)) {
            throw new ResponseStatusException(HttpStatus.NOT_ACCEPTABLE,
                    "Accept must include application/json and text/event-stream.");
        }
        if (requiresProtocolHeader(body)) {
            if (protocolVersion == null || protocolVersion.isBlank()) {
                throw new ResponseStatusException(HttpStatus.BAD_REQUEST, PROTOCOL_HEADER + " is required after initialize.");
            }
            if (!McpHandler.SUPPORTED.contains(protocolVersion.strip())) {
                throw new ResponseStatusException(HttpStatus.BAD_REQUEST, "Unsupported " + PROTOCOL_HEADER + ".");
            }
        }
        if (origin != null && !allowedOrigins.contains(origin)) throw new ResponseStatusException(HttpStatus.FORBIDDEN, "Origin not allowed.");
        var caller = ErpCaller.from(authentication);
        var tenant = caller.requireTenant();
        var response = handler.handle(body, new McpHandler.Caller(tenant, caller.subject()));
        return response.map(r -> ResponseEntity.ok().contentType(MediaType.APPLICATION_JSON).body(r.toString()))
                .orElseGet(() -> ResponseEntity.accepted().build());
    }

    private static boolean acceptsBothRepresentations(String value) {
        if (value == null || value.isBlank()) return false;
        try {
            var accepted = MediaType.parseMediaTypes(value);
            return accepted.stream().anyMatch(m -> m.getType().equalsIgnoreCase("application")
                    && m.getSubtype().equalsIgnoreCase("json") && m.getQualityValue() > 0)
                    && accepted.stream().anyMatch(m -> m.getType().equalsIgnoreCase("text")
                    && m.getSubtype().equalsIgnoreCase("event-stream") && m.getQualityValue() > 0);
        } catch (IllegalArgumentException e) {
            return false;
        }
    }

    /** Legacy MCP HTTP requires the negotiated version on every request after initialize. */
    private static boolean requiresProtocolHeader(String body) {
        try {
            JsonNode request = JSON.readTree(body);
            return request == null || !"initialize".equals(request.path("method").asText());
        } catch (Exception e) {
            return false; // Let the JSON-RPC parser return its normal parse error.
        }
    }

    /** No server-initiated stream and no sessions: GET is explicitly unsupported. */
    @GetMapping
    ResponseEntity<Void> get() { return ResponseEntity.status(HttpStatus.METHOD_NOT_ALLOWED).header("Allow", "POST").build(); }

    @DeleteMapping
    ResponseEntity<Void> delete() { return ResponseEntity.status(HttpStatus.METHOD_NOT_ALLOWED).header("Allow", "POST").build(); }
}
