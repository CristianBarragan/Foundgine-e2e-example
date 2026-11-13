package com.enterprise.erp.inventory;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.UUID;
import org.flywaydb.core.Flyway;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.jdbc.datasource.DriverManagerDataSource;

/** PostgreSQL integration test (ERP_TEST_JDBC, same contract as the other ERP DB tests). */
@EnabledIfEnvironmentVariable(named = "ERP_TEST_JDBC", matches = ".+")
class ErpInventoryServiceTest {
    private static JdbcClient jdbc;
    private static ErpInventoryService service;

    @BeforeAll static void setUp() {
        var support = com.enterprise.erp.ErpTestSupport.create();
        jdbc = support.jdbc();
        service = support.inventoryService();
    }

    private static void stock(String tenant, String sku, int onHand, int reserved) {
        jdbc.sql("INSERT INTO erp_products (tenant_id, sku, name, list_price, min_unit_price) VALUES (:t,:s,:n,10,5)")
                .param("t", tenant).param("s", sku).param("n", "Product " + sku).update();
        jdbc.sql("INSERT INTO erp_inventory (tenant_id, sku, on_hand, reserved) VALUES (:t,:s,:o,:r)")
                .param("t", tenant).param("s", sku).param("o", onHand).param("r", reserved).update();
    }

    @Test void reportsAvailableAsOnHandMinusReserved() {
        var tenant = "inv-" + UUID.randomUUID();
        stock(tenant, "SKU-A", 100, 30);
        var view = service.find(tenant, "SKU-A").orElseThrow();
        assertEquals(100, view.onHand());
        assertEquals(30, view.reserved());
        assertEquals(70, view.available());
    }

    @Test void neverShowsAnotherTenantsStock() {
        var mine = "inv-" + UUID.randomUUID();
        var theirs = "inv-" + UUID.randomUUID();
        stock(mine, "SKU-A", 5, 0);
        stock(theirs, "SKU-A", 999, 0);
        assertEquals(5, service.find(mine, "SKU-A").orElseThrow().onHand());
        assertEquals(1, service.list(mine).size());
        assertTrue(service.find(mine, "SKU-B").isEmpty());
    }
}
