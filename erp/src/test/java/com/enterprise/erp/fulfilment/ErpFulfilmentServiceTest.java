package com.enterprise.erp.fulfilment;

import static org.junit.jupiter.api.Assertions.*;
import java.math.BigDecimal;
import java.util.UUID;
import org.flywaydb.core.Flyway;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.jdbc.datasource.DataSourceTransactionManager;
import org.springframework.jdbc.datasource.DriverManagerDataSource;
import org.springframework.transaction.support.TransactionTemplate;

/** PostgreSQL integration tests. Configure ERP_TEST_JDBC to run. */
@EnabledIfEnvironmentVariable(named="ERP_TEST_JDBC",matches=".+")
class ErpFulfilmentServiceTest {
    private static JdbcClient jdbc;
    private static TransactionTemplate tx;
    private static ErpFulfilmentService service;
    private static com.enterprise.erp.ErpTestSupport support;
    @BeforeAll static void setup() {
        support=com.enterprise.erp.ErpTestSupport.create();
        jdbc=support.jdbc(); tx=support.tx();
        service=support.fulfilmentService();
    }
    @Test void shipmentInvoiceAndPaymentLifecyclePublishesOutboxEvents() {
        var f=fixture();
        var shipment=tx.execute(s->service.createShipment(f.tenant(),f.order(),new ErpFulfilmentService.CreateShipment("FastShip","TRACK-1","ship-key")));
        assertEquals("Packed",shipment.status());
        assertEquals(shipment.erpShipmentId(),tx.execute(s->service.createShipment(f.tenant(),f.order(),new ErpFulfilmentService.CreateShipment("FastShip","TRACK-1","ship-key"))).erpShipmentId());
        tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Shipped"));
        tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"InTransit"));
        var delivered=tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Delivered"));
        assertEquals(4,delivered.version());
        var invoice=tx.execute(s->service.issueInvoice(f.tenant(),f.order(),new ErpFulfilmentService.IssueInvoice("INV-TEST-1",null,null)));
        var payment=tx.execute(s->service.receivePayment(f.tenant(),invoice.erpInvoiceId(),new ErpFulfilmentService.ReceivePayment(new BigDecimal("40.00"),"payment-key-1")));
        assertEquals("Received",payment.status());
        assertEquals(payment.erpPaymentId(),tx.execute(s->service.receivePayment(f.tenant(),invoice.erpInvoiceId(),new ErpFulfilmentService.ReceivePayment(new BigDecimal("40.00"),"payment-key-1"))).erpPaymentId());
        assertEquals("Reconciled",tx.execute(s->service.reconcilePayment(f.tenant(),payment.erpPaymentId())).status());
        assertEquals("PartiallyPaid",jdbc.sql("SELECT status FROM erp_invoices WHERE erp_invoice_id=:id").param("id",invoice.erpInvoiceId()).query(String.class).single());
        assertEquals("Reversed",tx.execute(s->service.reversePayment(f.tenant(),payment.erpPaymentId())).status());
        assertEquals("Issued",jdbc.sql("SELECT status FROM erp_invoices WHERE erp_invoice_id=:id").param("id",invoice.erpInvoiceId()).query(String.class).single());
        var finalPayment=tx.execute(s->service.receivePayment(f.tenant(),invoice.erpInvoiceId(),new ErpFulfilmentService.ReceivePayment(new BigDecimal("100.00"),"payment-key-final")));
        tx.execute(s->service.reconcilePayment(f.tenant(),finalPayment.erpPaymentId()));
        assertEquals("Paid",jdbc.sql("SELECT status FROM erp_invoices WHERE erp_invoice_id=:id").param("id",invoice.erpInvoiceId()).query(String.class).single());
        assertTrue(jdbc.sql("SELECT count(*) FROM erp_temporal_event_outbox WHERE tenant_id=:tenant AND erp_order_id=:order").param("tenant",f.tenant()).param("order",f.order()).query(Integer.class).single()>=12);
    }
    @Test void receivedPaymentsReserveOutstandingBalanceBeforeReconciliation() {
        var f=fixture(); var invoice=tx.execute(s->service.issueInvoice(f.tenant(),f.order(),new ErpFulfilmentService.IssueInvoice("INV-TEST-2",null,null)));
        tx.execute(s->service.receivePayment(f.tenant(),invoice.erpInvoiceId(),new ErpFulfilmentService.ReceivePayment(new BigDecimal("60.00"),"reserve-1")));
        assertThrows(RuntimeException.class,()->tx.execute(s->service.receivePayment(f.tenant(),invoice.erpInvoiceId(),new ErpFulfilmentService.ReceivePayment(new BigDecimal("50.00"),"reserve-2"))));
    }

    @Test void idempotencyKeyCannotBeReusedWithDifferentShipmentOrPaymentPayload() {
        var f=fixture();
        tx.execute(s->service.createShipment(f.tenant(),f.order(),
            new ErpFulfilmentService.CreateShipment("FastShip","TRACK-IDEM","idem-shipment")));
        assertThrows(RuntimeException.class,()->tx.execute(s->service.createShipment(f.tenant(),f.order(),
            new ErpFulfilmentService.CreateShipment("OtherCarrier","TRACK-IDEM","idem-shipment"))));
        var invoice=tx.execute(s->service.issueInvoice(f.tenant(),f.order(),new ErpFulfilmentService.IssueInvoice("INV-IDEM",null,null)));
        tx.execute(s->service.receivePayment(f.tenant(),invoice.erpInvoiceId(),
            new ErpFulfilmentService.ReceivePayment(new BigDecimal("25.00"),"idem-payment")));
        assertThrows(RuntimeException.class,()->tx.execute(s->service.receivePayment(f.tenant(),invoice.erpInvoiceId(),
            new ErpFulfilmentService.ReceivePayment(new BigDecimal("30.00"),"idem-payment"))));
    }

    @Test void paymentAmountIsHeldAtTheStoredScaleInTheViewAndTheEvent() {
        var f=fixture();
        var invoice=tx.execute(s->service.issueInvoice(f.tenant(),f.order(),new ErpFulfilmentService.IssueInvoice("INV-SCALE",null,null)));
        var payment=tx.execute(s->service.receivePayment(f.tenant(),invoice.erpInvoiceId(),new ErpFulfilmentService.ReceivePayment(new BigDecimal("40"),"scale-key")));
        assertEquals(new BigDecimal("40.00"),payment.amount());   // BigDecimal.equals compares scale too
        assertEquals("40.00",jdbc.sql("SELECT payload->>'amount' FROM erp_temporal_event_outbox WHERE tenant_id=:t AND event_type='payment' AND entity_id=:id")
            .param("t",f.tenant()).param("id",payment.erpPaymentId()).query(String.class).single());
    }

    @Test void invoiceViewCarriesTheStoredTimestampsAndEventVersionsFollowTheStatus() {
        var f=fixture();
        var invoice=tx.execute(s->service.issueInvoice(f.tenant(),f.order(),new ErpFulfilmentService.IssueInvoice("INV-TS",null,null)));
        assertEquals(1,invoice.version());
        var stored=jdbc.sql("SELECT issued_at,due_at FROM erp_invoices WHERE erp_invoice_id=:id").param("id",invoice.erpInvoiceId())
            .query((rs,n)->new java.time.Instant[]{rs.getTimestamp(1).toInstant(),rs.getTimestamp(2).toInstant()}).single();
        assertEquals(stored[0],invoice.issuedAt());
        assertEquals(stored[1],invoice.dueAt());
        var payment=tx.execute(s->service.receivePayment(f.tenant(),invoice.erpInvoiceId(),new ErpFulfilmentService.ReceivePayment(new BigDecimal("100.00"),"ts-key")));
        tx.execute(s->service.reconcilePayment(f.tenant(),payment.erpPaymentId()));
        // Issued (v1) -> Paid (v2): the invoice event for the status change carries version 2.
        assertEquals(2,jdbc.sql("SELECT max(entity_version) FROM erp_temporal_event_outbox WHERE tenant_id=:t AND event_type='invoice' AND entity_id=:id")
            .param("t",f.tenant()).param("id",invoice.erpInvoiceId()).query(Integer.class).single());
    }

    @Test void repeatedShipmentTransitionIsIdempotent() {
        var f=fixture();
        var shipment=tx.execute(s->service.createShipment(f.tenant(),f.order(),
            new ErpFulfilmentService.CreateShipment("FastShip","TRACK-RETRY","transition-retry")));
        var shipped=tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Shipped"));
        var repeated=tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Shipped"));
        assertEquals(shipped.version(),repeated.version());
        assertEquals(shipped.status(),repeated.status());
    }

    // ---- shipping consumes reserved stock --------------------------------------------------------------------------

    private static Fixture stockedFixture(int qty) {
        var f=fixture();
        jdbc.sql("INSERT INTO erp_products(tenant_id,sku,name,list_price,min_unit_price) VALUES(:t,'SKU-X','X',10,5)").param("t",f.tenant()).update();
        jdbc.sql("INSERT INTO erp_inventory(tenant_id,sku,on_hand,reserved) VALUES(:t,'SKU-X',10,:r)").param("t",f.tenant()).param("r",qty).update();
        jdbc.sql("INSERT INTO stock_reservations(erp_order_id,tenant_id,sku,quantity) VALUES(:o,:t,'SKU-X',:q)").param("o",f.order()).param("t",f.tenant()).param("q",qty).update();
        return f;
    }
    private static int[] stock(Fixture f) {
        return jdbc.sql("SELECT on_hand,reserved FROM erp_inventory WHERE tenant_id=:t AND sku='SKU-X'").param("t",f.tenant())
            .query((rs,n)->new int[]{rs.getInt(1),rs.getInt(2)}).single();
    }
    private static ErpFulfilmentService.ShipmentView ship(Fixture f,String key) {
        return tx.execute(s->service.createShipment(f.tenant(),f.order(),new ErpFulfilmentService.CreateShipment("FastShip","TRACK-"+key,key)));
    }

    @Test void shippingConsumesTheReservedStockOnceAndOnlyWhenTheGoodsLeave() {
        var f=stockedFixture(4);
        var shipment=ship(f,"consume-1");
        assertArrayEquals(new int[]{10,4},stock(f));
        tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Shipped"));
        assertArrayEquals(new int[]{6,0},stock(f));
        tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Shipped"));
        tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"InTransit"));
        tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Delivered"));
        assertArrayEquals(new int[]{6,0},stock(f));
        assertTrue(jdbc.sql("SELECT consumed FROM stock_reservations WHERE erp_order_id=:o").param("o",f.order()).query(Boolean.class).single());
    }

    @Test void aSecondShipmentOfTheSameOrderDoesNotConsumeAgain() {
        var f=stockedFixture(4);
        var first=ship(f,"two-a"); var second=ship(f,"two-b");
        tx.execute(s->service.transitionShipment(f.tenant(),first.erpShipmentId(),"Shipped"));
        tx.execute(s->service.transitionShipment(f.tenant(),second.erpShipmentId(),"Shipped"));
        assertArrayEquals(new int[]{6,0},stock(f));
    }

    @Test void cancellingAPackedShipmentKeepsTheReservation() {
        var f=stockedFixture(4);
        var shipment=ship(f,"cancel-packed");
        tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Cancelled"));
        assertArrayEquals(new int[]{10,4},stock(f));
    }

    @Test void cancellingAnOrderAfterShippingDoesNotReleaseStockThatWasAlreadyConsumed() {
        var f=stockedFixture(4);
        var shipment=ship(f,"cancel-after");
        tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Shipped"));
        var orders=support.orderService();
        var result=tx.execute(s->orders.cancelOrder(new com.enterprise.erp.order.CancelOrderRequest(f.tenant(),1,f.order(),"customer withdrew")));
        assertTrue(result.cancelled());
        assertArrayEquals(new int[]{6,0},stock(f));
    }

    @Test void aCancelledOrderCannotShip() {
        var f=stockedFixture(4);
        var shipment=ship(f,"cancelled-order");
        var orders=support.orderService();
        tx.execute(s->orders.cancelOrder(new com.enterprise.erp.order.CancelOrderRequest(f.tenant(),1,f.order(),"withdrawn")));
        assertArrayEquals(new int[]{10,0},stock(f));
        assertThrows(RuntimeException.class,()->tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Shipped")));
        assertArrayEquals(new int[]{10,0},stock(f));
    }

    private static Fixture fixture() {
        String tenant="fulfil-"+UUID.randomUUID(),customer="C-"+UUID.randomUUID(),order="SO-"+UUID.randomUUID();
        jdbc.sql("INSERT INTO erp_customers(erp_customer_id,tenant_id,crm_customer_id,kind,name,status) VALUES(:id,:tenant,1,'Business','Test','Active')").param("id",customer).param("tenant",tenant).update();
        jdbc.sql("INSERT INTO sales_orders(erp_order_id,tenant_id,crm_quote_id,erp_customer_id,total,status,correlation_id) VALUES(:id,:tenant,1,:customer,100,'Open','corr')").param("id",order).param("tenant",tenant).param("customer",customer).update();
        return new Fixture(tenant,order);
    }
    private record Fixture(String tenant,String order){}
}
