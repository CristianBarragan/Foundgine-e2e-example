package com.enterprise.erp.fulfilment;

import com.enterprise.erp.persistence.AssignedIdEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;
import java.math.RoundingMode;
import java.time.Instant;
import java.util.UUID;

/** Payment against an invoice. {@code version} is bumped by hand, see {@link ErpShipment}. */
@Entity
@Table(name = "erp_payments")
public class ErpPayment extends AssignedIdEntity {
    @Id @Column(name = "erp_payment_id") private String erpPaymentId;
    @Column(name = "tenant_id") private String tenantId;
    @Column(name = "erp_order_id") private String erpOrderId;
    @Column(name = "erp_invoice_id") private String erpInvoiceId;
    @Column(name = "amount") private BigDecimal amount;
    @Column(name = "status") private String status;
    @Column(name = "idempotency_key") private String idempotencyKey;
    @Column(name = "version") private int version;
    // received_at comes from its column default; updated_at likewise on insert.
    @Column(name = "updated_at", insertable = false) private Instant updatedAt;

    protected ErpPayment() {}

    /** The amount is held at the column's scale (2, half-up like PostgreSQL), so the view and the event show what is stored. */
    public static ErpPayment received(String tenantId, String erpOrderId, String erpInvoiceId, BigDecimal amount, String idempotencyKey) {
        var p = new ErpPayment();
        p.erpPaymentId = "PAY-" + UUID.randomUUID();
        p.tenantId = tenantId; p.erpOrderId = erpOrderId; p.erpInvoiceId = erpInvoiceId;
        p.amount = amount.setScale(2, RoundingMode.HALF_UP);
        p.status = "Received"; p.idempotencyKey = idempotencyKey; p.version = 1;
        return p;
    }

    @Override public String getId() { return erpPaymentId; }
    public String getErpPaymentId() { return erpPaymentId; }
    public String getErpOrderId() { return erpOrderId; }
    public String getErpInvoiceId() { return erpInvoiceId; }
    public BigDecimal getAmount() { return amount; }
    public String getStatus() { return status; }
    public int getVersion() { return version; }

    public void moveTo(String next) { this.status = next; this.version++; this.updatedAt = Instant.now(); }
}
