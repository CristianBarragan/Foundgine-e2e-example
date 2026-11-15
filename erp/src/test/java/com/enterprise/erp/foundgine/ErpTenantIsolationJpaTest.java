package com.enterprise.erp.foundgine;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.enterprise.erp.security.ErpCaller;
import com.enterprise.erp.security.TenantRequiredException;
import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import jakarta.persistence.EntityManagerFactory;
import java.util.List;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.data.jpa.test.autoconfigure.DataJpaTest;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;

/**
 * Exercises the actual Foundgine JPA reader against Hibernate and H2, rather than stopping at plan generation.
 * The test deliberately supplies a contradictory caller filter to prove that it is AND-ed with the trusted
 * tenant predicate, not substituted for it.
 */
@DataJpaTest(properties = {
        "spring.jpa.hibernate.ddl-auto=create-drop",
        "spring.flyway.enabled=false"
})
@Transactional(propagation = Propagation.NOT_SUPPORTED)
class ErpTenantIsolationJpaTest {
    @Autowired private EntityManagerFactory entityManagerFactory;
    @Autowired private JdbcTemplate jdbc;

    private FoundgineOrm<ErpCaller> orm;

    @BeforeEach
    void setUp() {
        orm = ErpOrm.build();
        jdbc.update("delete from erp_customers");
        insertCustomer("CUS-A", "tenant-a", 101, "Tenant A customer");
        insertCustomer("CUS-B", "tenant-b", 202, "Tenant B customer");
    }

    @Test
    void readIsScopedToCallerTenant() {
        var rows = orm.reader(entityManagerFactory).read(
                caller("tenant-a"), "Customer", query -> query.select("Name", "TenantId"));

        assertEquals(1, rows.size(), "tenant-a must not see tenant-b's customer");
    }

    @Test
    void callerFilterCannotWidenTenantScope() {
        var rows = orm.reader(entityManagerFactory).read(
                caller("tenant-a"), "Customer",
                query -> query.select("Name", "TenantId").whereEq("TenantId", "tenant-b"));

        assertEquals(0, rows.size(), "the caller filter must be AND-ed with the trusted tenant predicate");
    }

    @Test
    void callerFilterWithinOwnTenantStillReturnsRows() {
        var rows = orm.reader(entityManagerFactory).read(
                caller("tenant-a"), "Customer",
                query -> query.select("Name").whereEq("TenantId", "tenant-a"));

        assertEquals(1, rows.size());
    }

    @Test
    void missingTenantIsRejectedBeforeJpaExecution() {
        var noTenant = new ErpCaller("test-user", null, List.of("erp-sales"));

        assertThrows(TenantRequiredException.class, () -> orm.reader(entityManagerFactory).read(
                noTenant, "Customer", query -> query.select("Name")));
    }

    private void insertCustomer(String id, String tenant, int crmId, String name) {
        jdbc.update("""
                insert into erp_customers
                    (erp_customer_id, tenant_id, crm_customer_id, kind, name, email, phone, status)
                values (?, ?, ?, 'Business', ?, '', '', 'Active')
                """, id, tenant, crmId, name);
    }

    private static ErpCaller caller(String tenant) {
        return new ErpCaller("test-user", tenant, List.of("erp-sales"));
    }
}
