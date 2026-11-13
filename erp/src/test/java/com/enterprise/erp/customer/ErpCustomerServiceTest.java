package com.enterprise.erp.customer;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;
import org.flywaydb.core.Flyway;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.jdbc.datasource.DriverManagerDataSource;

/** Needs PostgreSQL: set ERP_TEST_JDBC (e.g. jdbc:postgresql://localhost:5434/erp?user=erp&password=erp_dev_pw). */
@EnabledIfEnvironmentVariable(named = "ERP_TEST_JDBC", matches = ".+")
class ErpCustomerServiceTest {
    static JdbcClient jdbc;

    @BeforeAll
    static void setUp() {
        var support = com.enterprise.erp.ErpTestSupport.create();
        jdbc = support.jdbc();
        tx = support.tx();
        rawService = support.customerService();
    }

    static ErpCustomerService rawService;
    static org.springframework.transaction.support.TransactionTemplate tx;
    /** The service is not proxied here, so each call runs in a transaction exactly like @Transactional would. */
    static final Svc service = new Svc();
    static final class Svc {
        ErpCustomerResult createOrGet(CustomerSync in) { return tx.execute(s -> rawService.createOrGet(in)); }
    }

    static CustomerSync customer(String tenant, int crmId, String corr) {
        return new CustomerSync(1, tenant, crmId, 1, corr, "Business", "Acme Ltd", "a@acme.test", "", "Active");
    }

    @Test void duplicateMessageCreatesOneCustomer() {
        var tenant = "t-" + System.nanoTime();
        var first = service.createOrGet(customer(tenant, 1, "c1"));
        var again = service.createOrGet(customer(tenant, 1, "c1"));   // same message redelivered
        var retry = service.createOrGet(customer(tenant, 1, "c2"));   // later attempt, new correlation id
        assertTrue(first.created());
        assertFalse(again.created());
        assertEquals(first.erpCustomerId(), again.erpCustomerId());
        assertEquals(first.erpCustomerId(), retry.erpCustomerId());
        assertEquals(1, jdbc.sql("SELECT count(*) FROM erp_customers WHERE tenant_id = :t").param("t", tenant).query(Integer.class).single());
        assertEquals(2, jdbc.sql("SELECT count(*) FROM customer_inbox WHERE tenant_id = :t").param("t", tenant).query(Integer.class).single());
    }

    @Test void sameCrmIdInAnotherTenantIsAnotherCustomer() {
        var a = service.createOrGet(customer("ta-" + System.nanoTime(), 7, "x"));
        var b = service.createOrGet(customer("tb-" + System.nanoTime(), 7, "x"));
        assertNotEquals(a.erpCustomerId(), b.erpCustomerId());
    }

    @Test void invalidCustomerIsRejectedAndNothingIsWritten() {
        var tenant = "t-" + System.nanoTime();
        var bad = new CustomerSync(1, tenant, 9, 1, "c", "Business", "", "", "", "Active");
        assertThrows(ValidationFailedException.class, () -> service.createOrGet(bad));
        assertEquals(0, jdbc.sql("SELECT count(*) FROM erp_customers WHERE tenant_id = :t").param("t", tenant).query(Integer.class).single());
    }
}
