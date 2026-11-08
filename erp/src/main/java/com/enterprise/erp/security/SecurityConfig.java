package com.enterprise.erp.security;

import java.util.Collection;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.annotation.method.configuration.EnableMethodSecurity;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.core.GrantedAuthority;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationConverter;
import org.springframework.security.web.SecurityFilterChain;

/**
 * Keycloak bearer authentication for the ERP REST API. Mirrors the CRM: realm roles come from {@code realm_access.roles}
 * (mapped to ROLE_*), the tenant from {@code tenant_id}. Everything except health requires a valid token; role and
 * tenant checks are made per operation with {@code @PreAuthorize} and {@link ErpCaller}.
 */
@Configuration
@EnableMethodSecurity
class SecurityConfig {

    @Bean
    SecurityFilterChain api(HttpSecurity http) throws Exception {
        http.csrf(c -> c.disable()) // stateless bearer API: no cookies, no CSRF surface
                .sessionManagement(s -> s.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                .authorizeHttpRequests(a -> a
                        .requestMatchers("/api/v1/health", "/actuator/health", "/actuator/health/**").permitAll()
                        .anyRequest().authenticated())
                .oauth2ResourceServer(o -> o.jwt(j -> j.jwtAuthenticationConverter(converter())));
        return http.build();
    }

    static JwtAuthenticationConverter converter() {
        var converter = new JwtAuthenticationConverter();
        converter.setJwtGrantedAuthoritiesConverter(SecurityConfig::authorities);
        return converter;
    }

    static Collection<GrantedAuthority> authorities(Jwt jwt) {
        return ErpCaller.realmRoles(jwt).stream()
                .<GrantedAuthority>map(r -> new SimpleGrantedAuthority("ROLE_" + r)).toList();
    }
}
