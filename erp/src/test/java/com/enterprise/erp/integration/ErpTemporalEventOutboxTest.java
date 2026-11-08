package com.enterprise.erp.integration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;

import com.fasterxml.jackson.databind.ObjectMapper;
import java.util.UUID;
import org.flywaydb.core.Flyway;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.jdbc.datasource.DataSourceTransactionManager;
import org.springframework.jdbc.datasource.DriverManagerDataSource;
import org.springframework.transaction.support.TransactionTemplate;

/** PostgreSQL integration test. Uses the same ERP_TEST_JDBC contract as ErpOrderServiceTest. */
@EnabledIfEnvironmentVariable(named = "ERP_TEST_JDBC", matches = ".+")
class ErpTemporalEventOutboxTest {
    private static JdbcClient jdbc;
    private static TransactionTemplate tx;
    private static ErpTemporalEventOutbox outbox;

    @BeforeAll
    static void setUp() {
        var ds = new DriverManagerDataSource(System.getenv("ERP_TEST_JDBC"));
        Flyway.configure().dataSource(ds).load().migrate();
        jdbc = JdbcClient.create(ds);
        tx = new TransactionTemplate(new DataSourceTransactionManager(ds));
        outbox = new ErpTemporalEventOutbox(jdbc, new ObjectMapper().findAndRegisterModules());
    }

    @Test
    void duplicateEntityVersionReturnsTheSameEventAndCreatesOneOutboxRow() {
        String tenant = "outbox-" + UUID.randomUUID();
        String customer = "C-" + UUID.randomUUID();
        String order = "SO-" + UUID.randomUUID();
        jdbc.sql("INSERT INTO erp_customers (erp_customer_id, tenant_id, crm_customer_id, kind, name, status) VALUES (:id,:tenant,1,'Business','Test','Active')")
                .param("id", customer).param("tenant", tenant).update();
        jdbc.sql("INSERT INTO sales_orders (erp_order_id, tenant_id, crm_quote_id, erp_customer_id, total, status, correlation_id) VALUES (:id,:tenant,77,:customer,10,'Open','corr')")
                .param("id", order).param("tenant", tenant).param("customer", customer).update();

        var event = new ErpTemporalEventOutbox.ShipmentEvent(tenant, order, "SHIP-1", "Shipped", "Carrier", "TRACK-1", 1);
        var first = tx.execute(status -> outbox.shipment(event));
        var duplicate = tx.execute(status -> outbox.shipment(event));

        assertNotNull(first);
        assertEquals(first, duplicate);
        assertEquals(1, jdbc.sql("SELECT count(*) FROM erp_temporal_event_outbox WHERE tenant_id=:tenant AND event_type='shipment' AND entity_id='SHIP-1' AND entity_version=1")
                .param("tenant", tenant).query(Integer.class).single());
    }
}
