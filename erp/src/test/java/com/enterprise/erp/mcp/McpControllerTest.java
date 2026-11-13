package com.enterprise.erp.mcp;

import static org.mockito.Mockito.when;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.jwt;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.enterprise.erp.bots.ErpFacts;
import com.enterprise.erp.bots.StockFact;
import java.math.BigDecimal;
import java.util.Arrays;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.security.core.GrantedAuthority;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.request.RequestPostProcessor;

// Who may use /mcp and that tenant and caller come from the token only.
@SpringBootTest(properties = {
        "mcp.enabled=true",
        "spring.autoconfigure.exclude="
                + "org.springframework.boot.autoconfigure.jdbc.DataSourceAutoConfiguration,"
                + "org.springframework.boot.autoconfigure.flyway.FlywayAutoConfiguration,"
                + "org.springframework.boot.autoconfigure.jdbc.DataSourceTransactionManagerAutoConfiguration,"
                + "org.springframework.boot.hibernate.autoconfigure.HibernateJpaAutoConfiguration,"
                + "org.springframework.boot.data.jpa.autoconfigure.DataJpaRepositoriesAutoConfiguration"
})
@AutoConfigureMockMvc
class McpControllerTest extends com.enterprise.erp.NoDatabaseTestBase {
    private static final String CALL = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":"
            + "{\"name\":\"check_sku\",\"arguments\":{\"sku\":\"SKU-1\",\"tenantId\":\"other\"}}}";

    @Autowired MockMvc mvc;
    @MockitoBean ErpFacts facts;

    private static RequestPostProcessor caller(String tenant, String... roles) {
        return jwt().authorities(Arrays.stream(roles).<GrantedAuthority>map(r -> new SimpleGrantedAuthority("ROLE_" + r)).toList())
                .jwt(j -> {
                    j.subject("agent-1");
                    if (tenant != null) j.claim("tenant_id", tenant);
                    j.claim("realm_access", Map.of("roles", List.of(roles)));
                });
    }

    @Test void needsAToken() throws Exception {
        mvc.perform(post("/mcp").contentType("application/json").content(CALL)).andExpect(status().isUnauthorized());
    }

    @Test void nonErpRolesAreForbidden() throws Exception {
        mvc.perform(post("/mcp").contentType("application/json").content(CALL).with(caller("acme", "crm-admin"))).andExpect(status().isForbidden());
    }

    @Test void serviceAccountWithoutTenantIsRefused() throws Exception {
        mvc.perform(post("/mcp").contentType("application/json").content(CALL).with(caller(null, "erp-inventory"))).andExpect(status().isForbidden());
    }

    @Test void browserOriginIsRejectedUnlessAllowed() throws Exception {
        mvc.perform(post("/mcp").header("Origin", "https://evil.example").contentType("application/json").content(CALL)
                .with(caller("acme", "erp-inventory"))).andExpect(status().isForbidden());
    }

    @Test void toolRunsForTheTokensTenant() throws Exception {
        when(facts.factsFor("acme", List.of("SKU-1"))).thenReturn(Map.of("SKU-1", new StockFact("SKU-1", true, 7, new BigDecimal("90.00"))));
        mvc.perform(post("/mcp").contentType("application/json").content(CALL).with(caller("acme", "erp-inventory")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.result.isError").value(false))
                .andExpect(jsonPath("$.result.content[0].text").value(org.hamcrest.Matchers.containsString("\"available\":7")));
    }

    @Test void notificationIsAcceptedAndGetIsNotAllowed() throws Exception {
        mvc.perform(post("/mcp").contentType("application/json").content("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")
                .with(caller("acme", "erp-admin"))).andExpect(status().isAccepted());
        mvc.perform(get("/mcp").with(caller("acme", "erp-admin"))).andExpect(status().isMethodNotAllowed());
    }
}
