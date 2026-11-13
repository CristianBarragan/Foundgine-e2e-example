package com.enterprise.erp.integration;

import com.enterprise.erp.order.SalesOrderRepository;
import tools.jackson.core.JacksonException;
import tools.jackson.databind.ObjectMapper;
import java.math.BigDecimal;
import java.time.Instant;
import java.util.List;
import java.util.UUID;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * Append ERP-owned lifecycle facts in the same transaction as the corresponding domain write, and hand them to the relay
 * ({@link TemporalFulfilmentEventPublisher}) with an at-least-once lease: {@link #claimBatch} leases due events,
 * {@link #markPublished} settles one, {@link #markFailed} schedules its retry.
 */
@Service
public class ErpTemporalEventOutbox {
    private final ErpOutboxEventRepository events;
    private final SalesOrderRepository orders;
    private final ObjectMapper mapper;
    public ErpTemporalEventOutbox(ErpOutboxEventRepository events, SalesOrderRepository orders, ObjectMapper mapper) {
        this.events = events; this.orders = orders; this.mapper = mapper;
    }

    /** An event leased to the relay: what it needs to start the projection workflow and signal the parent. */
    public record Leased(UUID eventId, String tenantId, int quoteId, String eventType, String payload) {}

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
        int quoteId = orders.findCrmQuoteId(tenant, orderId)
            .orElseThrow(() -> new IllegalArgumentException("ERP order not found for tenant."));
        final String json;
        try { json = mapper.writeValueAsString(payload); }
        catch (JacksonException e) { throw new IllegalStateException("Could not serialize ERP temporal event.", e); }
        UUID id = UUID.randomUUID();
        if (events.insertIfAbsent(id, tenant, quoteId, orderId, type, entityId, version, json) == 1) return id;
        // Already appended (a retry, or a concurrent identical append): answer with the existing event's id.
        return events.findEventId(tenant, type, entityId, version)
            .orElseThrow(() -> new IllegalStateException("Outbox event " + type + "/" + entityId + "/v" + version + " vanished after a conflict."));
    }

    /**
     * Leases up to 50 due events for 30 seconds and counts the attempt. The select locks the rows and the lease update runs
     * in the same transaction, so two relay instances never lease the same event; the lease is committed before any
     * Temporal call is made.
     */
    @Transactional
    public List<Leased> claimBatch() {
        List<Object[]> rows = events.lockDue();
        if (rows.isEmpty()) return List.of();
        List<Leased> claimed = rows.stream()
            .map(r -> new Leased(UUID.fromString(String.valueOf(r[0])), (String) r[1], ((Number) r[2]).intValue(), (String) r[3], (String) r[4]))
            .toList();
        events.lease(claimed.stream().map(Leased::eventId).toList());
        return claimed;
    }

    @Transactional
    public void markPublished(UUID eventId) { events.markPublished(eventId); }

    @Transactional
    public void markFailed(UUID eventId, String error) { events.markFailed(eventId, error); }

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
