package com.enterprise.erp.fulfilment;

import com.enterprise.erp.persistence.AssignedIdEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;
import java.time.Instant;
import java.util.UUID;

/** Invoice for an order (one per order). {@code version} is bumped by hand, see {@link ErpShipment}. */
@Entity
@Table(name = "erp_invoices")
public class ErpInvoice extends AssignedIdEntity {
    @Id @Column(name = "erp_invoice_id") private String erpInvoiceId;
    @Column(name = "tenant_id") private String tenantId;
    @Column(name = "erp_order_id") private String erpOrderId;
    @Column(name = "invoice_number") private String invoiceNumber;
    @Column(name = "total") private BigDecimal total;
    @Column(name = "status") private String status;
    @Column(name = "issued_at") private Instant issuedAt;
    @Column(name = "due_at") private Instant dueAt;
    @Column(name = "version") private int version;
    @Column(name = "updated_at", insertable = false) private Instant updatedAt;

    protected ErpInvoice() {}

    /** issuedAt and dueAt must already be at microsecond precision (what timestamptz stores), so the view equals what is read back. */
    public static ErpInvoice issue(String tenantId, String erpOrderId, String invoiceNumber, BigDecimal total, Instant issuedAt, Instant dueAt) {
        var i = new ErpInvoice();
        i.erpInvoiceId = "INV-" + UUID.randomUUID();
        i.tenantId = tenantId; i.erpOrderId = erpOrderId; i.invoiceNumber = invoiceNumber; i.total = total;
        i.status = "Issued"; i.issuedAt = issuedAt; i.dueAt = dueAt; i.version = 1;
        return i;
    }

    @Override public String getId() { return erpInvoiceId; }
    public String getErpInvoiceId() { return erpInvoiceId; }
    public String getErpOrderId() { return erpOrderId; }
    public String getInvoiceNumber() { return invoiceNumber; }
    public BigDecimal getTotal() { return total; }
    public String getStatus() { return status; }
    public Instant getIssuedAt() { return issuedAt; }
    public Instant getDueAt() { return dueAt; }
    public int getVersion() { return version; }

    public void moveTo(String next) { this.status = next; this.version++; this.updatedAt = Instant.now(); }
}
