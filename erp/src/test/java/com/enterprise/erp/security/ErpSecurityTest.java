package com.enterprise.erp.security;

import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.jwt;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.web.servlet.MockMvc;

// Token validation itself (issuer, audience, signature) is the resource server's; these tests pin our rules:
// who may reach what, and how roles and tenant are read from the claims.
@SpringBootTest(properties = {
	    "spring.autoconfigure.exclude=" +
	        "org.springframework.boot.autoconfigure.jdbc.DataSourceAutoConfiguration," +
	        "org.springframework.boot.autoconfigure.flyway.FlywayAutoConfiguration," +
	        "org.springframework.boot.autoconfigure.jdbc.DataSourceTransactionManagerAutoConfiguration," +
	        "org.springframework.boot.hibernate.autoconfigure.HibernateJpaAutoConfiguration," +
	        "org.springframework.boot.data.jpa.autoconfigure.DataJpaRepositoriesAutoConfiguration"
	})
@AutoConfigureMockMvc
class ErpSecurityTest extends com.enterprise.erp.NoDatabaseTestBase {
    @Autowired MockMvc mvc;

    @Test void healthNeedsNoToken() throws Exception { mvc.perform(get("/api/v1/health")).andExpect(status().isOk()); }

    @Test void whoamiRejectsMissingToken() throws Exception { mvc.perform(get("/api/v1/whoami")).andExpect(status().isUnauthorized()); }

    @Test void whoamiReadsSubjectTenantAndRealmRoles() throws Exception {
        mvc.perform(get("/api/v1/whoami").with(jwt().jwt(j -> j.subject("u1").claim("tenant_id", "acme")
                        .claim("realm_access", Map.of("roles", List.of("erp-sales"))))))
                .andExpect(status().isOk()).andExpect(jsonPath("$.tenant").value("acme")).andExpect(jsonPath("$.roles[0]").value("erp-sales"));
    }

    @Test void malformedRolesClaimYieldsNoRoles() throws Exception {
        mvc.perform(get("/api/v1/whoami").with(jwt().jwt(j -> j.subject("u1").claim("realm_access", "oops"))))
                .andExpect(status().isOk()).andExpect(jsonPath("$.roles").isEmpty());
    }
}
