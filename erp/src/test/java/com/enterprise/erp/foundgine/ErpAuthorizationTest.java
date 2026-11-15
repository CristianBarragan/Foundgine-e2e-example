package com.enterprise.erp.foundgine;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.enterprise.erp.security.ErpCaller;
import com.enterprise.erp.security.TenantRequiredException;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationException;
import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import java.util.List;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class ErpAuthorizationTest {
    private FoundgineOrm<ErpCaller> orm;

    @BeforeEach
    void setUp() {
        orm = ErpOrm.build();
    }

    @Test
    void validatesAllSixMappedEntitiesAtBuildTime() {
        assertDoesNotThrow(ErpOrm::build);
    }

    @Test
    void salesCanReadOrders() {
        assertDoesNotThrow(() -> plan("erp-sales", "SalesOrder", "Status", "Total"));
    }

    @Test
    void salesCannotReadInvoices() {
        assertThrows(SemanticAuthorizationException.class,
                () -> plan("erp-sales", "Invoice", "Status"));
    }

    @Test
    void financeCanReadInvoices() {
        assertDoesNotThrow(() -> plan("erp-finance", "Invoice", "Status", "Total"));
    }

    @Test
    void readRoleMatrixIsEnforcedForEveryMappedEntity() {
        // Keep this matrix in sync with ErpAuthorization. Each positive case must plan successfully;
        // each negative case must be rejected by the semantic authorizer before provider execution.
        assertReadAllowed("erp-sales", "Customer", "SalesOrder", "Return", "Shipment");
        assertReadDenied("erp-sales", "Invoice", "Payment");

        assertReadAllowed("erp-finance", "Customer", "SalesOrder", "Invoice", "Payment");
        assertReadDenied("erp-finance", "Return", "Shipment");

        assertReadAllowed("erp-inventory", "Return", "Shipment");
        assertReadDenied("erp-inventory", "Customer", "SalesOrder", "Invoice", "Payment");

        assertReadAllowed("erp-admin", "Customer", "SalesOrder", "Return", "Shipment", "Invoice", "Payment");
        assertReadAllowed("enterprise-admin", "Customer", "SalesOrder", "Return", "Shipment", "Invoice", "Payment");
    }

    @Test
    void internalFieldsAreAdminOnly() {
        assertThrows(IllegalStateException.class,
                () -> plan("erp-sales", "SalesOrder", "CorrelationId"));
        assertThrows(IllegalStateException.class,
                () -> plan("erp-inventory", "Shipment", "IdempotencyKey"));
        assertDoesNotThrow(() -> plan("erp-admin", "SalesOrder", "CorrelationId"));
        assertDoesNotThrow(() -> plan("erp-admin", "Shipment", "IdempotencyKey"));
    }

    @Test
    void missingTenantFailsClosedForPlanningAndExecutionContext() {
        var caller = new ErpCaller("service-account", null, List.of("erp-admin"));
        assertThrows(TenantRequiredException.class,
                () -> orm.plan(caller, "SalesOrder", q -> q.select("Status")));
        assertThrows(TenantRequiredException.class,
                () -> new ErpAuthorization().executionContext(caller));
    }

    private void assertReadAllowed(String role, String... entities) {
        for (String entity : entities) {
            assertDoesNotThrow(() -> plan(role, entity, "Status"), role + " should read " + entity);
        }
    }

    private void assertReadDenied(String role, String... entities) {
        for (String entity : entities) {
            assertThrows(SemanticAuthorizationException.class,
                    () -> plan(role, entity, "Status"), role + " must not read " + entity);
        }
    }

    private void plan(String role, String entity, String... fields) {
        var caller = new ErpCaller("test-user", "tenant-a", List.of(role));
        orm.plan(caller, entity, query -> query.select(fields));
    }
}
