package com.enterprise.erp.returns;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.Instant;

/**
 * ERP return. Created by {@link ErpReturnRepository#insertIfAbsent} (the ERP mints the id from erp_return_seq); afterwards
 * only completion changes it.
 */
@Entity
@Table(name = "erp_returns")
public class ErpReturn {
    @Id @Column(name = "erp_return_id") private String erpReturnId;
    @Column(name = "tenant_id") private String tenantId;
    @Column(name = "crm_return_id") private int crmReturnId;
    @Column(name = "erp_order_id") private String erpOrderId;
    @Column(name = "status") private String status;
    @Column(name = "reason") private String reason;
    @Column(name = "note") private String note;
    @Column(name = "correlation_id") private String correlationId;
    @Column(name = "updated_at") private Instant updatedAt;

    protected ErpReturn() {}

    public String getErpReturnId() { return erpReturnId; }
    public String getStatus() { return status; }
    public String getNote() { return note; }

    /** Goods received: Authorized -> Completed. The caller has locked the row and checked the status. */
    public void complete() {
        this.status = "Completed";
        this.updatedAt = Instant.now();
    }
}
