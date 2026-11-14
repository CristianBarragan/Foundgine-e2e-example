package com.enterprise.erp.mcp;

import com.enterprise.erp.bots.ErpFacts;
import com.enterprise.erp.security.ErpCaller;
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

    McpController(ErpFacts facts, @Value("${mcp.allowed-origins:}") String allowedOrigins) {
        this.handler = new McpHandler("erp-mcp", "1.0.0", ErpMcpTools.over(facts));
        this.allowedOrigins = Arrays.stream(allowedOrigins.split(",")).map(String::strip).filter(s -> !s.isEmpty()).toList();
    }

    @PostMapping(consumes = MediaType.APPLICATION_JSON_VALUE)
    @PreAuthorize("hasAnyRole('erp-inventory', 'erp-admin', 'enterprise-admin')")
    ResponseEntity<String> post(@RequestBody String body, @RequestHeader(value = "Origin", required = false) String origin,
                                Authentication authentication) {
        if (origin != null && !allowedOrigins.contains(origin)) throw new ResponseStatusException(HttpStatus.FORBIDDEN, "Origin not allowed.");
        var caller = ErpCaller.from(authentication);
        var tenant = caller.requireTenant();
        var response = handler.handle(body, new McpHandler.Caller(tenant, caller.subject()));
        return response.map(r -> ResponseEntity.ok().contentType(MediaType.APPLICATION_JSON).body(r.toString()))
                .orElseGet(() -> ResponseEntity.accepted().build());
    }

    /** No server-initiated stream and no sessions: the spec allows 405 for both. */
    @GetMapping
    ResponseEntity<Void> get() { return ResponseEntity.status(HttpStatus.METHOD_NOT_ALLOWED).header("Allow", "POST").build(); }

    @DeleteMapping
    ResponseEntity<Void> delete() { return ResponseEntity.status(HttpStatus.METHOD_NOT_ALLOWED).header("Allow", "POST").build(); }
}
