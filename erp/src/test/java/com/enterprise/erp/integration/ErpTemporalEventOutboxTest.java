package com.enterprise.erp.integration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.enterprise.erp.ErpTestSupport;
import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.transaction.support.TransactionTemplate;

/**
 * PostgreSQL integration test. Uses the same ERP_TEST_JDBC contract as ErpOrderServiceTest. The relay methods are called
 * inside tx() because the repositories are not proxied here (see ErpTestSupport). The claim takes the 50 oldest due events
 * of ALL tenants, so each test back-dates its own events to be sure they are among them, and looks only at its own tenant.
 */
@EnabledIfEnvironmentVariable(named = "ERP_TEST_JDBC", matches = ".+")
class ErpTemporalEventOutboxTest {
    private static JdbcClient jdbc;
    private static TransactionTemplate tx;
    private static ErpTemporalEventOutbox outbox;

    @BeforeAll
    static void setUp() {
        var support = ErpTestSupport.create();
        jdbc = support.jdbc();
        tx = support.tx();
        outbox = support.outbox();
    }

    /** A tenant with one open order (CRM quote 77). */
    private static String[] orderFixture() {
        String tenant = "outbox-" + UUID.randomUUID();
        String customer = "C-" + UUID.randomUUID();
        String order = "SO-" + UUID.randomUUID();
        jdbc.sql("INSERT INTO erp_customers (erp_customer_id, tenant_id, crm_customer_id, kind, name, status) VALUES (:id,:tenant,1,'Business','Test','Active')")
                .param("id", customer).param("tenant", tenant).update();
        jdbc.sql("INSERT INTO sales_orders (erp_order_id, tenant_id, crm_quote_id, erp_customer_id, total, status, correlation_id) VALUES (:id,:tenant,77,:customer,10,'Open','corr')")
                .param("id", order).param("tenant", tenant).param("customer", customer).update();
        return new String[] {tenant, order};
    }

    private static UUID append(String tenant, String order, String shipmentId, int version) {
        var event = new ErpTemporalEventOutbox.ShipmentEvent(tenant, order, shipmentId, "Shipped", "Carrier", "TRACK-1", version);
        return tx.execute(status -> outbox.shipment(event));
    }

    private static void backdate(String tenant) {
        jdbc.sql("UPDATE erp_temporal_event_outbox SET created_at = TIMESTAMPTZ '2000-01-01 00:00:00+00' WHERE tenant_id = :t")
                .param("t", tenant).update();
    }

    private static List<ErpTemporalEventOutbox.Leased> claimFor(String tenant) {
        return tx.execute(status -> outbox.claimBatch()).stream().filter(e -> e.tenantId().equals(tenant)).toList();
    }

    @Test
    void duplicateEntityVersionReturnsTheSameEventAndCreatesOneOutboxRow() {
        var f = orderFixture();
        var event = new ErpTemporalEventOutbox.ShipmentEvent(f[0], f[1], "SHIP-1", "Shipped", "Carrier", "TRACK-1", 1);
        var first = tx.execute(status -> outbox.shipment(event));
        var duplicate = tx.execute(status -> outbox.shipment(event));

        assertNotNull(first);
        assertEquals(first, duplicate);
        assertEquals(1, jdbc.sql("SELECT count(*) FROM erp_temporal_event_outbox WHERE tenant_id=:tenant AND event_type='shipment' AND entity_id='SHIP-1' AND entity_version=1")
                .param("tenant", f[0]).query(Integer.class).single());
    }

    @Test
    void appendStoresTheQuoteAndTheJsonPayload() {
        var f = orderFixture();
        var id = append(f[0], f[1], "SHIP-PAYLOAD", 1);
        assertEquals(77, jdbc.sql("SELECT crm_quote_id FROM erp_temporal_event_outbox WHERE event_id=:id").param("id", id).query(Integer.class).single());
        assertEquals("TRACK-1", jdbc.sql("SELECT payload->>'trackingNumber' FROM erp_temporal_event_outbox WHERE event_id=:id").param("id", id).query(String.class).single());
    }

    @Test
    void claimLeasesADueEventOnceAndCountsTheAttempt() {
        var f = orderFixture();
        var id = append(f[0], f[1], "SHIP-CLAIM", 1);
        backdate(f[0]);

        var claimed = claimFor(f[0]);
        assertEquals(1, claimed.size());
        var row = claimed.get(0);
        assertEquals(id, row.eventId());
        assertEquals(77, row.quoteId());
        assertEquals("shipment", row.eventType());
        assertTrue(row.payload().contains("SHIP-CLAIM"));
        assertEquals(1, jdbc.sql("SELECT attempts FROM erp_temporal_event_outbox WHERE event_id=:id").param("id", id).query(Integer.class).single());
        assertTrue(jdbc.sql("SELECT locked_until > now() FROM erp_temporal_event_outbox WHERE event_id=:id").param("id", id).query(Boolean.class).single());

        assertTrue(claimFor(f[0]).isEmpty(), "a leased event is not handed out again");
    }

    @Test
    void publishedEventsLeaveTheQueue() {
        var f = orderFixture();
        var id = append(f[0], f[1], "SHIP-DONE", 1);
        backdate(f[0]);
        assertEquals(1, claimFor(f[0]).size());

        tx.execute(status -> { outbox.markPublished(id); return null; });

        assertTrue(jdbc.sql("SELECT published_at IS NOT NULL AND locked_until IS NULL AND last_error IS NULL FROM erp_temporal_event_outbox WHERE event_id=:id")
                .param("id", id).query(Boolean.class).single());
        assertTrue(claimFor(f[0]).isEmpty());
    }

    @Test
    void aFailedEventKeepsItsErrorAndIsRetriedLater() {
        var f = orderFixture();
        var id = append(f[0], f[1], "SHIP-FAIL", 1);
        backdate(f[0]);
        assertEquals(1, claimFor(f[0]).size());

        // Pretend it has already failed many times: the backoff is min(300, 2^min(attempts, 8)) = 256 seconds, far enough
        // in the future that the check below cannot race the clock.
        jdbc.sql("UPDATE erp_temporal_event_outbox SET attempts = 8 WHERE event_id=:id").param("id", id).update();

        tx.execute(status -> { outbox.markFailed(id, "temporal unavailable"); return null; });

        assertEquals("temporal unavailable", jdbc.sql("SELECT last_error FROM erp_temporal_event_outbox WHERE event_id=:id").param("id", id).query(String.class).single());
        assertTrue(jdbc.sql("SELECT locked_until IS NULL AND published_at IS NULL FROM erp_temporal_event_outbox WHERE event_id=:id").param("id", id).query(Boolean.class).single());
        assertTrue(jdbc.sql("SELECT available_at > now() + interval '200 seconds' AND available_at < now() + interval '300 seconds' FROM erp_temporal_event_outbox WHERE event_id=:id")
                .param("id", id).query(Boolean.class).single());
        assertTrue(claimFor(f[0]).isEmpty());
    }
}
