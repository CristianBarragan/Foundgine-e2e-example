package com.enterprise.erp.order;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;
import java.time.Instant;

/** ERP sales order. Created by SalesOrderRepository.insertIfAbsent; afterwards only cancellation changes it. */
@Entity
@Table(name = "sales_orders")
public class SalesOrder {
    @Id @Column(name = "erp_order_id") private String erpOrderId;
    @Column(name = "tenant_id") private String tenantId;
    @Column(name = "crm_quote_id") private int crmQuoteId;
    @Column(name = "erp_customer_id") private String erpCustomerId;
    @Column(name = "total") private BigDecimal total;
    @Column(name = "status") private String status;
    @Column(name = "cancel_reason") private String cancelReason;
    @Column(name = "correlation_id") private String correlationId;
    @Column(name = "updated_at") private Instant updatedAt;

    protected SalesOrder() {}

    public String getErpOrderId() { return erpOrderId; }
    public String getErpCustomerId() { return erpCustomerId; }
    public BigDecimal getTotal() { return total; }
    public String getStatus() { return status; }

    public void cancel(String reason) {
        this.status = "Cancelled";
        this.cancelReason = reason;
        this.updatedAt = Instant.now();
    }
}
