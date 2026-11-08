package com.enterprise.erp.integration;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.math.BigDecimal;
import java.time.Instant;
import java.util.UUID;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/** Append ERP-owned lifecycle facts in the same transaction as the corresponding domain write. */
@Service
public class ErpTemporalEventOutbox {
    private final JdbcClient jdbc;
    private final ObjectMapper mapper;
    public ErpTemporalEventOutbox(JdbcClient jdbc, ObjectMapper mapper) { this.jdbc = jdbc; this.mapper = mapper; }

    @Transactional public UUID shipment(ShipmentEvent event) {
        validate(event.tenantId(), event.erpOrderId(), event.erpShipmentId(), event.version());
        if (!java.util.Set.of("Packed", "Shipped", "InTransit", "Delivered", "Cancelled").contains(event.status()))
            throw new IllegalArgumentException("Unsupported shipment status.");
        return append("shipment", event.tenantId(), event.erpOrderId(), event.erpShipmentId(), event.version(), event);
    }
    @Transactional public UUID invoice(InvoiceEvent event) {
        validate(event.tenantId(), event.erpOrderId(), event.erpInvoiceId(), event.version());
        if (event.number() == null || event.number().isBlank() || event.total() == null || event.total().signum() < 0 ||
                event.issuedAt() == null || !java.util.Set.of("Issued", "PartiallyPaid", "Paid", "Overdue", "Void").contains(event.status()))
            throw new IllegalArgumentException("Invalid invoice event.");
        return append("invoice", event.tenantId(), event.erpOrderId(), event.erpInvoiceId(), event.version(), event);
    }
    @Transactional public UUID payment(PaymentEvent event) {
        validate(event.tenantId(), event.erpOrderId(), event.erpPaymentId(), event.version());
        if (event.erpInvoiceId() == null || event.erpInvoiceId().isBlank() || event.amount() == null || event.amount().signum() < 0 ||
                !java.util.Set.of("Received", "Reconciled", "Reversed").contains(event.status()))
            throw new IllegalArgumentException("Invalid payment event.");
        return append("payment", event.tenantId(), event.erpOrderId(), event.erpPaymentId(), event.version(), event);
    }

    private UUID append(String type, String tenant, String orderId, String entityId, int version, Object payload) {
        int quoteId = jdbc.sql("SELECT crm_quote_id FROM sales_orders WHERE tenant_id=:tenant AND erp_order_id=:order")
            .param("tenant", tenant).param("order", orderId).query(Integer.class).optional()
            .orElseThrow(() -> new IllegalArgumentException("ERP order not found for tenant."));
        final String json;
        try { json = mapper.writeValueAsString(payload); }
        catch (JsonProcessingException e) { throw new IllegalStateException("Could not serialize ERP temporal event.", e); }
        UUID id = UUID.randomUUID();
        return jdbc.sql("""
            INSERT INTO erp_temporal_event_outbox
                (event_id, tenant_id, crm_quote_id, erp_order_id, event_type, entity_id, entity_version, payload)
            VALUES (:id, :tenant, :quote, :order, :type, :entity, :version, CAST(:payload AS jsonb))
            ON CONFLICT (tenant_id, event_type, entity_id, entity_version)
            DO UPDATE SET event_id = erp_temporal_event_outbox.event_id
            RETURNING event_id
            """).param("id", id).param("tenant", tenant).param("quote", quoteId).param("order", orderId)
            .param("type", type).param("entity", entityId).param("version", version).param("payload", json)
            .query(UUID.class).single();
    }
    private static void validate(String tenant, String order, String entity, int version) {
        if (tenant == null || tenant.isBlank() || order == null || order.isBlank() || entity == null || entity.isBlank() || version < 1)
            throw new IllegalArgumentException("tenantId, erpOrderId, entity id and positive version are required.");
    }

    public record ShipmentEvent(String tenantId, String erpOrderId, String erpShipmentId, String status,
                                String carrier, String trackingNumber, int version) {}
    public record InvoiceEvent(String tenantId, String erpOrderId, String erpInvoiceId, String number,
                               BigDecimal total, String status, Instant issuedAt, Instant dueAt, int version) {}
    public record PaymentEvent(String tenantId, String erpOrderId, String erpInvoiceId, String erpPaymentId,
                               BigDecimal amount, String status, int version) {}
}
