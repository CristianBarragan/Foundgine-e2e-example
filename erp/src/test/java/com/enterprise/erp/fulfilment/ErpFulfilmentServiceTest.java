package com.enterprise.erp.fulfilment;

import static org.junit.jupiter.api.Assertions.*;
import com.enterprise.erp.integration.ErpTemporalEventOutbox;
import com.fasterxml.jackson.databind.ObjectMapper;
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
    @BeforeAll static void setup() {
        var ds=new DriverManagerDataSource(System.getenv("ERP_TEST_JDBC"));
        Flyway.configure().dataSource(ds).load().migrate(); jdbc=JdbcClient.create(ds);
        tx=new TransactionTemplate(new DataSourceTransactionManager(ds));
        service=new ErpFulfilmentService(jdbc,new ErpTemporalEventOutbox(jdbc,new ObjectMapper().findAndRegisterModules()));
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

    @Test void repeatedShipmentTransitionIsIdempotent() {
        var f=fixture();
        var shipment=tx.execute(s->service.createShipment(f.tenant(),f.order(),
            new ErpFulfilmentService.CreateShipment("FastShip","TRACK-RETRY","transition-retry")));
        var shipped=tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Shipped"));
        var repeated=tx.execute(s->service.transitionShipment(f.tenant(),shipment.erpShipmentId(),"Shipped"));
        assertEquals(shipped.version(),repeated.version());
        assertEquals(shipped.status(),repeated.status());
    }

    private static Fixture fixture() {
        String tenant="fulfil-"+UUID.randomUUID(),customer="C-"+UUID.randomUUID(),order="SO-"+UUID.randomUUID();
        jdbc.sql("INSERT INTO erp_customers(erp_customer_id,tenant_id,crm_customer_id,kind,name,status) VALUES(:id,:tenant,1,'Business','Test','Active')").param("id",customer).param("tenant",tenant).update();
        jdbc.sql("INSERT INTO sales_orders(erp_order_id,tenant_id,crm_quote_id,erp_customer_id,total,status,correlation_id) VALUES(:id,:tenant,1,:customer,100,'Open','corr')").param("id",order).param("tenant",tenant).param("customer",customer).update();
        return new Fixture(tenant,order);
    }
    private record Fixture(String tenant,String order){}
}
