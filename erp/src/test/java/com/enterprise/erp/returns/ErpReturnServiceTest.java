package com.enterprise.erp.returns;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.enterprise.erp.customer.CustomerSync;
import com.enterprise.erp.customer.ErpCustomerService;
import com.enterprise.erp.customer.ValidationFailedException;
import com.enterprise.erp.order.ErpOrderService;
import com.enterprise.erp.order.OrderRequest;
import java.math.BigDecimal;
import java.util.List;
import org.flywaydb.core.Flyway;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.jdbc.datasource.DataSourceTransactionManager;
import org.springframework.jdbc.datasource.DriverManagerDataSource;
import org.springframework.transaction.support.TransactionTemplate;

/** Needs PostgreSQL: set ERP_TEST_JDBC. Calls run in a TransactionTemplate (the service is not proxied here). */
@EnabledIfEnvironmentVariable(named = "ERP_TEST_JDBC", matches = ".+")
class ErpReturnServiceTest {
    static ErpReturnService service;
    static ErpOrderService orders;
    static ErpCustomerService customers;
    static JdbcClient jdbc;
    static TransactionTemplate tx;

    @BeforeAll
    static void setUp() {
        var support = com.enterprise.erp.ErpTestSupport.create();
        jdbc = support.jdbc();
        service = support.returnService();
        orders = support.orderService();
        customers = support.customerService();
        tx = support.tx();
    }

    record Fixture(String tenant, String customer, String order) {}

    /** A tenant with an order of 5 x A (stock 20, 5 reserved). */
    static Fixture fixture() {
        var t = "t-" + System.nanoTime();
        var c = tx.execute(s -> customers.createOrGet(new CustomerSync(1, t, 42, 1, "c", "Business", "Acme", "", "", "Active"))).erpCustomerId();
        jdbc.sql("INSERT INTO erp_products (tenant_id, sku, name, list_price, min_unit_price) VALUES (:t, 'A', 'A', 100, 80)").param("t", t).update();
        jdbc.sql("INSERT INTO erp_inventory (tenant_id, sku, on_hand) VALUES (:t, 'A', 20)").param("t", t).update();
        var o = tx.execute(s -> orders.createOrderWithReservation(new OrderRequest(1, t, 7, 1, 42, c, "o1", new BigDecimal("500.00"),
                List.of(new OrderRequest.Line("A", "", 5, new BigDecimal("100.00")))))).erpOrderId();
        return new Fixture(t, c, o);
    }

    static ReturnRequest ret(Fixture f, int id, int qty) {
        return new ReturnRequest(1, f.tenant(), id, 7, 42, f.customer(), f.order(), "r" + id, "damaged", List.of(new ReturnRequest.Line("A", qty)));
    }

    static ErpReturnDecision authorize(ReturnRequest r) { return tx.execute(s -> service.authorize(r)); }

    static int onHand(String t) {
        return jdbc.sql("SELECT on_hand FROM erp_inventory WHERE tenant_id = :t AND sku = 'A'").param("t", t).query(Integer.class).single();
    }

    @Test void authorizeIsIdempotentOnTheCrmReturnId() {
        var f = fixture();
        var first = authorize(ret(f, 1, 2));
        var again = authorize(ret(f, 1, 2));
        assertEquals("Authorized", first.decision());
        assertTrue(first.created());
        assertFalse(again.created());
        assertEquals(first.erpReturnId(), again.erpReturnId());
        assertEquals(1, jdbc.sql("SELECT count(*) FROM erp_returns WHERE tenant_id = :t").param("t", f.tenant()).query(Integer.class).single());
    }

    @Test void totalReturnsCannotExceedWhatWasOrdered() {
        var f = fixture();
        assertEquals("Authorized", authorize(ret(f, 1, 3)).decision());
        var second = authorize(ret(f, 2, 3));   // only 2 remain
        assertEquals("Rejected", second.decision());
        assertTrue(second.note().contains("only 2 of 5"));
        assertEquals("Authorized", authorize(ret(f, 3, 2)).decision());
        assertEquals("Authorized", authorize(ret(f, 1, 3)).decision());   // replay keeps the stored answer
        assertEquals("Rejected", authorize(ret(f, 2, 3)).decision());
    }

    @Test void unknownOrderOrWrongCustomerIsRejectedNotFailed() {
        var f = fixture();
        var bad = new ReturnRequest(1, f.tenant(), 9, 7, 42, f.customer(), "SO-0", "c", "x", List.of(new ReturnRequest.Line("A", 1)));
        assertEquals("Rejected", authorize(bad).decision());
        var other = new ReturnRequest(1, f.tenant(), 10, 7, 42, "ERP-0", f.order(), "c", "x", List.of(new ReturnRequest.Line("A", 1)));
        assertEquals("Rejected", authorize(other).decision());
    }

    @Test void malformedRequestFails() {
        var f = fixture();
        var bad = new ReturnRequest(1, f.tenant(), 0, 7, 42, f.customer(), f.order(), "c", "x", List.of());
        assertThrows(ValidationFailedException.class, () -> authorize(bad));
    }

    @Test void completeAddsStockOnceAndOnlyForAuthorizedReturns() {
        var f = fixture();
        var d = authorize(ret(f, 1, 2));
        assertEquals(20, onHand(f.tenant()));
        var first = tx.execute(s -> service.complete(new CompleteReturnRequest(f.tenant(), d.erpReturnId())));
        var again = tx.execute(s -> service.complete(new CompleteReturnRequest(f.tenant(), d.erpReturnId())));
        assertTrue(first.completed());
        assertFalse(again.completed());
        assertEquals(22, onHand(f.tenant()));   // added once

        var rejected = authorize(ret(f, 2, 99));
        assertThrows(ValidationFailedException.class, () -> tx.execute(s -> service.complete(new CompleteReturnRequest(f.tenant(), rejected.erpReturnId()))));
        assertThrows(ValidationFailedException.class, () -> tx.execute(s -> service.complete(new CompleteReturnRequest("other", d.erpReturnId()))));
    }
}
