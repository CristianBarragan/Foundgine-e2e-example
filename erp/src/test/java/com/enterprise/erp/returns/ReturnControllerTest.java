package com.enterprise.erp.returns;

import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyInt;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;
import static org.mockito.Mockito.when;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.jwt;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.util.Arrays;
import java.util.Collection;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.core.convert.converter.Converter;
import org.springframework.http.HttpStatus;
import org.springframework.security.core.GrantedAuthority;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.request.RequestPostProcessor;
import org.springframework.web.server.ResponseStatusException;

// Who may confirm receipt, and that tenant and sender come from the token only. The signal itself is the service's port.
@SpringBootTest(properties = {
        "spring.autoconfigure.exclude=org.springframework.boot.autoconfigure.jdbc.DataSourceAutoConfiguration,"
                + "org.springframework.boot.autoconfigure.flyway.FlywayAutoConfiguration,"
                + "org.springframework.boot.autoconfigure.jdbc.DataSourceTransactionManagerAutoConfiguration"})
@AutoConfigureMockMvc
class ReturnControllerTest {
    private static final String URL = "/api/v1/returns/7/goods-received";
    private static final String LATE = "/api/v1/returns/7/received-late";

    @Autowired MockMvc mvc;
    @MockitoBean org.springframework.jdbc.core.simple.JdbcClient jdbcClient;
    @MockitoBean GoodsReceivedService service;
    @MockitoBean ExpiredReturnService expired;

    // The jwt() post-processor bypasses the app's JwtAuthenticationConverter, so the ROLE_* authorities that the real
    // converter derives from realm_access.roles are set explicitly here (the same mapping SecurityConfig applies).
    private static RequestPostProcessor caller(String tenant, String... roles) {
        return jwt().authorities((Converter<Jwt, Collection<GrantedAuthority>>) Arrays.stream(roles).map(r -> new SimpleGrantedAuthority("ROLE_" + r)).toList())
                .jwt(j -> {
                    j.subject("wh-user");
                    if (tenant != null) j.claim("tenant_id", tenant);
                    j.claim("realm_access", Map.of("roles", List.of(roles)));
                });
    }

    @Test void needsAToken() throws Exception {
        mvc.perform(post(URL)).andExpect(status().isUnauthorized());
        verifyNoInteractions(service);
    }

    @Test void salesRoleCannotConfirmReceipt() throws Exception {
        mvc.perform(post(URL).with(caller("acme", "erp-sales"))).andExpect(status().isForbidden());
        verifyNoInteractions(service);
    }

    @Test void crmRolesCannotConfirmReceipt() throws Exception {
        mvc.perform(post(URL).with(caller("acme", "crm-admin"))).andExpect(status().isForbidden());
        verifyNoInteractions(service);
    }

    @Test void serviceAccountWithoutTenantIsRefused() throws Exception {
        mvc.perform(post(URL).with(caller(null, "erp-inventory"))).andExpect(status().isForbidden());
        verifyNoInteractions(service);
    }

    @Test void inventoryRoleSignalsWithTenantAndSubjectFromTheToken() throws Exception {
        when(service.receive("acme", 7, "wh-user")).thenReturn(GoodsReceivedService.Outcome.SIGNALLED);
        // A tenant or sender in the request body or query string is not read.
        mvc.perform(post(URL + "?tenant=other&receivedBy=mallory").contentType("application/json")
                        .content("{\"tenantId\":\"other\",\"receivedBy\":\"mallory\"}").with(caller("acme", "erp-inventory")))
                .andExpect(status().isAccepted()).andExpect(jsonPath("$.status").value("signalled"));
        verify(service).receive("acme", 7, "wh-user");
    }

    @Test void adminRolesAreAccepted() throws Exception {
        when(service.receive(anyString(), anyInt(), anyString())).thenReturn(GoodsReceivedService.Outcome.SIGNALLED);
        mvc.perform(post(URL).with(caller("acme", "erp-admin"))).andExpect(status().isAccepted());
        mvc.perform(post(URL).with(caller("acme", "enterprise-admin"))).andExpect(status().isAccepted());
    }

    @Test void completedReturnAnswersOk() throws Exception {
        when(service.receive("acme", 7, "wh-user")).thenReturn(GoodsReceivedService.Outcome.ALREADY_COMPLETED);
        mvc.perform(post(URL).with(caller("acme", "erp-inventory")))
                .andExpect(status().isOk()).andExpect(jsonPath("$.status").value("alreadyCompleted"));
    }

    @Test void unknownReturnIsNotFoundAndConflictsAreReported() throws Exception {
        when(service.receive("acme", 7, "wh-user")).thenThrow(new ResponseStatusException(HttpStatus.NOT_FOUND, "Return not found."));
        mvc.perform(post(URL).with(caller("acme", "erp-inventory"))).andExpect(status().isNotFound());
        when(service.receive("acme", 7, "wh-user")).thenThrow(new ResponseStatusException(HttpStatus.CONFLICT, "x"));
        mvc.perform(post(URL).with(caller("acme", "erp-inventory"))).andExpect(status().isConflict());
    }

    @Test void badReturnIdIsRejectedBeforeTheService() throws Exception {
        mvc.perform(post("/api/v1/returns/0/goods-received").with(caller("acme", "erp-inventory"))).andExpect(status().isBadRequest());
        mvc.perform(post("/api/v1/returns/abc/goods-received").with(caller("acme", "erp-inventory"))).andExpect(status().isBadRequest());
        verify(service, never()).receive(any(), anyInt(), any());
    }

    // ---- operator path (expired wait): admins only ----

    @Test void lateReceiptNeedsAToken() throws Exception {
        mvc.perform(post(LATE)).andExpect(status().isUnauthorized());
        verifyNoInteractions(expired);
    }

    @Test void warehouseRoleCannotUseTheOperatorPath() throws Exception {
        mvc.perform(post(LATE).with(caller("acme", "erp-inventory"))).andExpect(status().isForbidden());
        mvc.perform(post(LATE).with(caller("acme", "erp-sales"))).andExpect(status().isForbidden());
        verifyNoInteractions(expired);
    }

    @Test void lateReceiptWithoutATenantIsRefused() throws Exception {
        mvc.perform(post(LATE).with(caller(null, "erp-admin"))).andExpect(status().isForbidden());
        verifyNoInteractions(expired);
    }

    @Test void adminStartsTheResolutionWithTenantAndSubjectFromTheToken() throws Exception {
        when(expired.resolveReceivedLate("acme", 7, "wh-user")).thenReturn(ExpiredReturnService.Outcome.STARTED);
        mvc.perform(post(LATE + "?tenant=other&resolvedBy=mallory").contentType("application/json")
                        .content("{\"tenantId\":\"other\",\"erpReturnId\":\"RET-1\"}").with(caller("acme", "erp-admin")))
                .andExpect(status().isAccepted()).andExpect(jsonPath("$.status").value("resolving"));
        verify(expired).resolveReceivedLate("acme", 7, "wh-user");
    }

    @Test void enterpriseAdminIsAcceptedAndRepeatsAnswerOk() throws Exception {
        when(expired.resolveReceivedLate("acme", 7, "wh-user"))
                .thenReturn(ExpiredReturnService.Outcome.ALREADY_IN_PROGRESS, ExpiredReturnService.Outcome.ALREADY_COMPLETED);
        mvc.perform(post(LATE).with(caller("acme", "enterprise-admin")))
                .andExpect(status().isOk()).andExpect(jsonPath("$.status").value("alreadyInProgress"));
        mvc.perform(post(LATE).with(caller("acme", "enterprise-admin")))
                .andExpect(status().isOk()).andExpect(jsonPath("$.status").value("alreadyCompleted"));
    }

    @Test void lateReceiptConflictsAndBadIdsAreReported() throws Exception {
        when(expired.resolveReceivedLate("acme", 7, "wh-user")).thenThrow(new ResponseStatusException(HttpStatus.CONFLICT, "x"));
        mvc.perform(post(LATE).with(caller("acme", "erp-admin"))).andExpect(status().isConflict());
        mvc.perform(post("/api/v1/returns/0/received-late").with(caller("acme", "erp-admin"))).andExpect(status().isBadRequest());
        verify(expired, never()).resolveReceivedLate(any(), eq(0), any());
    }
}
