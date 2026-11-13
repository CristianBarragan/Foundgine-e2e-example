package com.enterprise.erp.fulfilment;

import com.enterprise.erp.customer.ValidationFailedException;
import com.enterprise.erp.integration.ErpTemporalEventOutbox;
import com.enterprise.erp.inventory.ErpInventoryRepository;
import com.enterprise.erp.order.SalesOrderRepository;
import com.enterprise.erp.order.StockReservationRepository;
import java.math.BigDecimal;
import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.List;
import java.util.Set;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * ERP-owned shipment, invoice and payment lifecycle. Domain writes and outbox events commit atomically.
 *
 * <p>Lock order, everywhere: the order row first (shipping, invoicing), then the shipment / invoice / payment row. A payment
 * change locks the payment, then its invoice. Entity versions are bumped by the entities themselves on every status change
 * and the views (and so the events) are built from the locked entity, so they always carry the version that was written.
 */
@Service
public class ErpFulfilmentService {
    private final ErpShipmentRepository shipments;
    private final ErpInvoiceRepository invoices;
    private final ErpPaymentRepository payments;
    private final SalesOrderRepository orders;
    private final StockReservationRepository reservations;
    private final ErpInventoryRepository inventory;
    private final ErpTemporalEventOutbox outbox;

    public ErpFulfilmentService(ErpShipmentRepository shipments, ErpInvoiceRepository invoices, ErpPaymentRepository payments,
                                SalesOrderRepository orders, StockReservationRepository reservations,
                                ErpInventoryRepository inventory, ErpTemporalEventOutbox outbox) {
        this.shipments = shipments; this.invoices = invoices; this.payments = payments;
        this.orders = orders; this.reservations = reservations; this.inventory = inventory; this.outbox = outbox;
    }

    @Transactional
    public ShipmentView createShipment(String tenant, String orderId, CreateShipment in) {
        requireOpenOrder(tenant, orderId);
        if (in == null || blank(in.carrier()) || blank(in.trackingNumber()) || blank(in.idempotencyKey()) || in.idempotencyKey().length() > 128)
            throw invalid("carrier, trackingNumber and an Idempotency-Key (max 128 chars) are required.");
        var existing = shipments.findByTenantIdAndIdempotencyKey(tenant, in.idempotencyKey());
        if (existing.isPresent()) {
            var prior = existing.get();
            if (!orderId.equals(prior.getErpOrderId()) ||
                    !in.carrier().trim().equals(prior.getCarrier()) ||
                    !in.trackingNumber().trim().equals(prior.getTrackingNumber()))
                throw invalid("Idempotency-Key was already used with a different shipment command.");
            return view(prior);
        }
        var created = shipments.save(ErpShipment.packed(tenant, orderId, in.carrier().trim(), in.trackingNumber().trim(), in.idempotencyKey()));
        ShipmentView view = view(created);
        publish(tenant, view);
        return view;
    }

    @Transactional
    public ShipmentView transitionShipment(String tenant, String id, String next) {
        var shipment = shipments.lockByTenantAndId(tenant, id).orElseThrow(() -> invalid("Shipment not found for this tenant."));
        // A retried transition after a lost HTTP response is a successful no-op.
        if (shipment.getStatus().equals(next)) return view(shipment);
        if (!allowed(shipment.getStatus(), next)) throw invalid("Invalid shipment transition " + shipment.getStatus() + " -> " + next + ".");
        // The goods leave the warehouse when the shipment is first Shipped: that is when reserved stock is consumed.
        if ("Shipped".equals(next)) consumeReservation(tenant, shipment.getErpOrderId());
        shipment.moveTo(next);
        ShipmentView updated = view(shipment);
        publish(tenant, updated);
        return updated;
    }

    @Transactional
    public InvoiceView issueInvoice(String tenant, String orderId, IssueInvoice in) {
        var order = orders.lockByTenantAndId(tenant, orderId).orElseThrow(() -> invalid("Order not found for this tenant."));
        if (!"Open".equals(order.getStatus())) throw invalid("Only open orders can be invoiced.");
        var prior = invoices.findByTenantIdAndErpOrderId(tenant, orderId);
        if (prior.isPresent()) return view(prior.get());
        // timestamptz keeps microseconds: truncate here so the view and the event equal what is stored.
        Instant issued = (in != null && in.issuedAt() != null ? in.issuedAt() : Instant.now()).truncatedTo(ChronoUnit.MICROS);
        Instant due = (in != null && in.dueAt() != null ? in.dueAt() : issued.plus(30, ChronoUnit.DAYS)).truncatedTo(ChronoUnit.MICROS);
        if (!due.isAfter(issued)) throw invalid("Invoice dueAt must be after issuedAt.");
        String number = in != null && !blank(in.invoiceNumber()) ? in.invoiceNumber().trim()
                : "INV-" + java.util.UUID.randomUUID().toString().substring(0, 8).toUpperCase();
        var created = invoices.save(ErpInvoice.issue(tenant, orderId, number, order.getTotal(), issued, due));
        InvoiceView view = view(created);
        publish(tenant, view);
        return view;
    }

    @Transactional
    public PaymentView receivePayment(String tenant, String invoiceId, ReceivePayment in) {
        if (in == null || in.amount() == null || in.amount().signum() <= 0) throw invalid("A payment amount greater than zero is required.");
        var invoice = invoices.lockByTenantAndId(tenant, invoiceId).orElseThrow(() -> invalid("Invoice not found for this tenant."));
        if (blank(in.idempotencyKey()) || in.idempotencyKey().length() > 128) throw invalid("An Idempotency-Key (max 128 chars) is required.");
        var existing = payments.findByTenantIdAndIdempotencyKey(tenant, in.idempotencyKey());
        if (existing.isPresent()) {
            var prior = existing.get();
            if (!invoiceId.equals(prior.getErpInvoiceId()) || in.amount().compareTo(prior.getAmount()) != 0)
                throw invalid("Idempotency-Key was already used with a different payment command.");
            return view(prior);
        }
        if (Set.of("Paid", "Void").contains(invoice.getStatus())) throw invalid("Payments cannot be added to a paid or void invoice.");
        BigDecimal committed = sum(tenant, invoiceId, List.of("Received", "Reconciled"));
        if (in.amount().compareTo(invoice.getTotal().subtract(committed)) > 0) throw invalid("Payment exceeds the outstanding invoice balance.");
        var created = payments.save(ErpPayment.received(tenant, invoice.getErpOrderId(), invoiceId, in.amount(), in.idempotencyKey()));
        PaymentView view = view(created);
        publish(tenant, view);
        return view;
    }

    @Transactional
    public PaymentView reconcilePayment(String tenant, String paymentId) {
        var payment = payments.lockByTenantAndId(tenant, paymentId).orElseThrow(() -> invalid("Payment not found for this tenant."));
        if ("Reconciled".equals(payment.getStatus())) return view(payment);
        if (!"Received".equals(payment.getStatus())) throw invalid("Only received payments can be reconciled.");
        payment.moveTo("Reconciled");
        var invoice = invoices.lockByTenantAndId(tenant, payment.getErpInvoiceId())
                .orElseThrow(() -> new IllegalStateException("Invoice " + payment.getErpInvoiceId() + " of payment " + paymentId + " is missing."));
        BigDecimal paid = sum(tenant, payment.getErpInvoiceId(), List.of("Reconciled"));   // flushes the status change above first
        if (!"Void".equals(invoice.getStatus())) {
            String status = paid.compareTo(invoice.getTotal()) >= 0 ? "Paid" : "PartiallyPaid";
            if (!status.equals(invoice.getStatus())) {
                invoice.moveTo(status);
                publish(tenant, view(invoice));
            }
        }
        PaymentView updated = view(payment);
        publish(tenant, updated);
        return updated;
    }

    @Transactional
    public PaymentView reversePayment(String tenant, String paymentId) {
        var payment = payments.lockByTenantAndId(tenant, paymentId).orElseThrow(() -> invalid("Payment not found for this tenant."));
        if ("Reversed".equals(payment.getStatus())) return view(payment);
        if (!Set.of("Received", "Reconciled").contains(payment.getStatus())) throw invalid("Only received or reconciled payments can be reversed.");
        payment.moveTo("Reversed");
        var invoice = invoices.lockByTenantAndId(tenant, payment.getErpInvoiceId())
                .orElseThrow(() -> new IllegalStateException("Invoice " + payment.getErpInvoiceId() + " of payment " + paymentId + " is missing."));
        BigDecimal paid = sum(tenant, payment.getErpInvoiceId(), List.of("Reconciled"));   // flushes the status change above first
        String status = paid.signum() == 0 ? "Issued" : paid.compareTo(invoice.getTotal()) >= 0 ? "Paid" : "PartiallyPaid";
        if (!"Void".equals(invoice.getStatus()) && !status.equals(invoice.getStatus())) {
            invoice.moveTo(status);
            publish(tenant, view(invoice));
        }
        PaymentView updated = view(payment);
        publish(tenant, updated);
        return updated;
    }

    private void publish(String tenant, ShipmentView x) {
        outbox.shipment(new ErpTemporalEventOutbox.ShipmentEvent(tenant, x.erpOrderId(), x.erpShipmentId(), x.status(), x.carrier(), x.trackingNumber(), x.version()));
    }

    private void publish(String tenant, InvoiceView x) {
        outbox.invoice(new ErpTemporalEventOutbox.InvoiceEvent(tenant, x.erpOrderId(), x.erpInvoiceId(), x.invoiceNumber(), x.total(), x.status(), x.issuedAt(), x.dueAt(), x.version()));
    }

    private void publish(String tenant, PaymentView x) {
        outbox.payment(new ErpTemporalEventOutbox.PaymentEvent(tenant, x.erpOrderId(), x.erpInvoiceId(), x.erpPaymentId(), x.amount(), x.status(), x.version()));
    }

    private static ShipmentView view(ErpShipment s) {
        return new ShipmentView(s.getErpShipmentId(), s.getErpOrderId(), s.getStatus(), s.getCarrier(), s.getTrackingNumber(), s.getVersion());
    }

    private static InvoiceView view(ErpInvoice i) {
        return new InvoiceView(i.getErpInvoiceId(), i.getErpOrderId(), i.getInvoiceNumber(), i.getTotal(), i.getStatus(), i.getIssuedAt(), i.getDueAt(), i.getVersion());
    }

    private static PaymentView view(ErpPayment p) {
        return new PaymentView(p.getErpPaymentId(), p.getErpOrderId(), p.getErpInvoiceId(), p.getAmount(), p.getStatus(), p.getVersion());
    }

    private BigDecimal sum(String tenant, String invoiceId, List<String> statuses) {
        BigDecimal sum = payments.sumAmount(tenant, invoiceId, statuses);
        return sum == null ? BigDecimal.ZERO : sum;
    }

    private void requireOpenOrder(String tenant, String orderId) {
        if (!orders.existsByTenantIdAndErpOrderIdAndStatus(tenant, orderId, "Open")) throw invalid("Open order not found for this tenant.");
    }

    private static boolean allowed(String from, String to) {
        if (to == null) return false;
        return switch (from) {
            case "Packed" -> Set.of("Shipped", "Cancelled").contains(to);
            case "Shipped" -> Set.of("InTransit", "Delivered", "Cancelled").contains(to);
            case "InTransit" -> Set.of("Delivered", "Cancelled").contains(to);
            default -> false;
        };
    }

    /**
     * Shipments are whole-order, so the first shipment to ship consumes the order's reservation (on_hand and reserved both
     * drop by the reserved quantity) and later shipments find nothing left to consume. The order row is locked first, the
     * same lock order as cancelOrder, so a concurrent cancellation cannot release stock that is being consumed. Holding that
     * order lock is also what serialises the reservation rows between shipping and cancelling.
     */
    private void consumeReservation(String tenant, String orderId) {
        var order = orders.lockByTenantAndId(tenant, orderId).orElseThrow(() -> invalid("Order not found for this tenant."));
        if (!"Open".equals(order.getStatus())) throw invalid("Only an open order can be shipped.");
        for (var held : reservations.findByErpOrderIdAndReleasedFalseAndConsumedFalseOrderBySku(orderId)) {
            if (inventory.consume(tenant, held.getSku(), held.getQuantity()) != 1)
                throw invalid("Inventory for " + held.getSku() + " does not hold the reservation for this order.");
            held.consume();
        }
    }

    private static boolean blank(String s) { return s == null || s.isBlank(); }

    private static ValidationFailedException invalid(String m) { return new ValidationFailedException(List.of(m)); }

    public record CreateShipment(String carrier, String trackingNumber, String idempotencyKey) {}
    public record IssueInvoice(String invoiceNumber, Instant issuedAt, Instant dueAt) {}
    public record ReceivePayment(BigDecimal amount, String idempotencyKey) {}
    public record ShipmentView(String erpShipmentId, String erpOrderId, String status, String carrier, String trackingNumber, int version) {}
    public record InvoiceView(String erpInvoiceId, String erpOrderId, String invoiceNumber, BigDecimal total, String status, Instant issuedAt, Instant dueAt, int version) {}
    public record PaymentView(String erpPaymentId, String erpOrderId, String erpInvoiceId, BigDecimal amount, String status, int version) {}
}
