package com.enterprise.erp.order;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.enterprise.erp.customer.CustomerSync;
import com.enterprise.erp.customer.ErpCustomerService;
import com.enterprise.erp.customer.ValidationFailedException;
import java.math.BigDecimal;
import java.util.List;
import java.util.concurrent.Callable;
import java.util.concurrent.Executors;
import org.flywaydb.core.Flyway;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.jdbc.datasource.DriverManagerDataSource;
import org.springframework.jdbc.datasource.DataSourceTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;

/**
 * Needs PostgreSQL: set ERP_TEST_JDBC. The service is not run through a Spring proxy here, so each call is wrapped in a
 * TransactionTemplate to get the same all-or-nothing behaviour as @Transactional.
 */
@EnabledIfEnvironmentVariable(named = "ERP_TEST_JDBC", matches = ".+")
class ErpOrderServiceTest {
    static ErpOrderService service;
    static ErpCustomerService customers;
    static JdbcClient jdbc;
    static TransactionTemplate tx;

    @BeforeAll
    static void setUp() {
        var support = com.enterprise.erp.ErpTestSupport.create();
        jdbc = support.jdbc();
        service = support.orderService();
        customers = support.customerService();
        tx = support.tx();
    }

    /** Seeds a tenant with an active customer and two products (A: 10 in stock, B: 1 in stock); returns the ERP customer id. */
    static String seed(String tenant) {
        var c = tx.execute(s -> customers.createOrGet(new CustomerSync(1, tenant, 42, 1, "c", "Business", "Acme", "", "", "Active")));
        for (var p : new String[][] {{"A", "10"}, {"B", "1"}}) {
            jdbc.sql("INSERT INTO erp_products (tenant_id, sku, name, list_price, min_unit_price) VALUES (:t, :s, :s, 100, 80)")
                    .param("t", tenant).param("s", p[0]).update();
            jdbc.sql("INSERT INTO erp_inventory (tenant_id, sku, on_hand) VALUES (:t, :s, :n)")
                    .param("t", tenant).param("s", p[0]).param("n", Integer.parseInt(p[1])).update();
        }
        return c.erpCustomerId();
    }

    static OrderRequest order(String tenant, String erpCustomer, int quote, String corr, String sku, int qty) {
        var total = new BigDecimal("100.00").multiply(BigDecimal.valueOf(qty));
        return new OrderRequest(1, tenant, quote, 1, 42, erpCustomer, corr, total,
                List.of(new OrderRequest.Line(sku, "", qty, new BigDecimal("100.00"))));
    }

    static ErpOrderResult place(OrderRequest r) { return tx.execute(s -> service.createOrderWithReservation(r)); }

    static int reserved(String tenant, String sku) {
        return jdbc.sql("SELECT reserved FROM erp_inventory WHERE tenant_id = :t AND sku = :s").param("t", tenant).param("s", sku).query(Integer.class).single();
    }

    @Test void duplicateRequestCreatesOneOrderAndOneReservation() {
        var t = "t-" + System.nanoTime();
        var cust = seed(t);
        var first = place(order(t, cust, 7, "c1", "A", 3));
        var again = place(order(t, cust, 7, "c1", "A", 3));   // redelivered
        var retry = place(order(t, cust, 7, "c2", "A", 3));   // later attempt, new correlation id
        assertTrue(first.created());
        assertFalse(again.created());
        assertEquals(first.erpOrderId(), again.erpOrderId());
        assertEquals(first.erpOrderId(), retry.erpOrderId());
        assertEquals(3, reserved(t, "A"));   // reserved once, not three times
        assertEquals(1, jdbc.sql("SELECT count(*) FROM sales_orders WHERE tenant_id = :t").param("t", t).query(Integer.class).single());
    }

    @Test void insufficientStockRollsEverythingBack() {
        var t = "t-" + System.nanoTime();
        var cust = seed(t);
        assertThrows(InsufficientStockException.class, () -> place(order(t, cust, 8, "c1", "B", 2)));
        assertEquals(0, jdbc.sql("SELECT count(*) FROM sales_orders WHERE tenant_id = :t").param("t", t).query(Integer.class).single());
        assertEquals(0, reserved(t, "B"));
    }

    @Test void multiLineOrderIsAllOrNothing() {
        var t = "t-" + System.nanoTime();
        var cust = seed(t);
        var lines = List.of(new OrderRequest.Line("A", "", 2, new BigDecimal("100.00")), new OrderRequest.Line("B", "", 5, new BigDecimal("100.00")));
        var r = new OrderRequest(1, t, 9, 1, 42, cust, "c1", new BigDecimal("700.00"), lines);
        assertThrows(InsufficientStockException.class, () -> place(r));
        assertEquals(0, reserved(t, "A"));   // A was reserved first inside the transaction, then rolled back with B's failure
    }

    @Test void priceBelowTheFloorAndWrongCustomerAreRejected() {
        var t = "t-" + System.nanoTime();
        var cust = seed(t);
        var cheap = new OrderRequest(1, t, 10, 1, 42, cust, "c1", new BigDecimal("70.00"),
                List.of(new OrderRequest.Line("A", "", 1, new BigDecimal("70.00"))));
        assertThrows(ValidationFailedException.class, () -> place(cheap));
        assertThrows(ValidationFailedException.class, () -> place(order(t, "ERP-0", 11, "c2", "A", 1)));
        assertEquals(0, reserved(t, "A"));
    }

    @Test void tenantsAreIsolated() {
        var a = "ta-" + System.nanoTime();
        var b = "tb-" + System.nanoTime();
        var custA = seed(a);
        seed(b);
        // tenant b trying to order on tenant a's customer id is refused
        assertThrows(ValidationFailedException.class, () -> place(order(b, custA, 1, "c", "A", 1)));
    }

    @Test void cancelReleasesTheReservationAndIsIdempotent() {
        var t = "t-" + System.nanoTime();
        var cust = seed(t);
        var o = place(order(t, cust, 12, "c1", "A", 4));
        assertEquals(4, reserved(t, "A"));
        var first = tx.execute(s -> service.cancelOrder(new CancelOrderRequest(t, 12, o.erpOrderId(), "test")));
        var again = tx.execute(s -> service.cancelOrder(new CancelOrderRequest(t, 12, o.erpOrderId(), "test")));
        assertTrue(first.cancelled());
        assertFalse(again.cancelled());
        assertEquals(0, reserved(t, "A"));   // released once, not twice
        assertThrows(ValidationFailedException.class, () -> place(order(t, cust, 12, "c9", "A", 4)));   // a cancelled order is not replayed as live
    }

    @Test void concurrentOrdersNeverOverReserve() throws Exception {
        var t = "t-" + System.nanoTime();
        var cust = seed(t);   // A has 10 on hand
        var pool = Executors.newFixedThreadPool(8);
        try {
            var tasks = new java.util.ArrayList<Callable<Boolean>>();
            for (int q = 100; q < 108; q++) {
                final int quote = q;
                tasks.add(() -> {
                    try { place(order(t, cust, quote, "c" + quote, "A", 2)); return true; }
                    catch (InsufficientStockException e) { return false; }
                });
            }
            long ok = pool.invokeAll(tasks).stream().filter(f -> { try { return f.get(); } catch (Exception e) { throw new RuntimeException(e); } }).count();
            assertEquals(5, ok);                 // 10 units / 2 per order
            assertEquals(10, reserved(t, "A"));
        } finally { pool.shutdown(); }
    }
}
