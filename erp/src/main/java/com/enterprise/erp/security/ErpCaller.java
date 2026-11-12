package com.enterprise.erp.security;

import java.util.List;
import java.util.Map;
import org.springframework.security.core.Authentication;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationToken;

/** Who is calling and for which tenant, read only from the validated token. */
public record ErpCaller(String subject, String tenantId, List<String> roles) {
    public static final String TENANT_CLAIM = "tenant_id";

    /** Realm roles from {@code realm_access.roles}; a malformed claim yields no roles (deny by default). */
    static List<String> realmRoles(Jwt jwt) {
        if (!(jwt.getClaim("realm_access") instanceof Map<?, ?> access)) return List.of();
        if (!(access.get("roles") instanceof List<?> roles)) return List.of();
        return roles.stream().filter(String.class::isInstance).map(String.class::cast).toList();
    }

    public static ErpCaller from(Authentication authentication) {
        if (!(authentication instanceof JwtAuthenticationToken token)) throw new IllegalStateException("No JWT authentication.");
        Jwt jwt = token.getToken();
        return new ErpCaller(jwt.getSubject(), jwt.getClaimAsString(TENANT_CLAIM), realmRoles(jwt));
    }

    /** The tenant for tenant-scoped data; tokens without one (service accounts) cannot touch tenant data. */
    public String requireTenant() {
        if (tenantId == null || tenantId.isBlank()) throw new TenantRequiredException();
        return tenantId;
    }
}
