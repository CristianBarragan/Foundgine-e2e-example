package com.enterprise.erp.order;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.math.BigDecimal;
import java.util.Objects;

@Entity
@Table(name = "sales_order_lines")
@IdClass(SalesOrderLine.Key.class)
public class SalesOrderLine {
    public static class Key implements Serializable {
        private String erpOrderId;
        private int lineNo;
        public Key() {}
        public Key(String erpOrderId, int lineNo) { this.erpOrderId = erpOrderId; this.lineNo = lineNo; }
        @Override public boolean equals(Object o) { return o instanceof Key k && lineNo == k.lineNo && Objects.equals(erpOrderId, k.erpOrderId); }
        @Override public int hashCode() { return Objects.hash(erpOrderId, lineNo); }
    }

    @Id @Column(name = "erp_order_id") private String erpOrderId;
    @Id @Column(name = "line_no") private int lineNo;
    @Column(name = "tenant_id") private String tenantId;
    @Column(name = "sku") private String sku;
    @Column(name = "quantity") private int quantity;
    @Column(name = "unit_price") private BigDecimal unitPrice;

    protected SalesOrderLine() {}

    public SalesOrderLine(String erpOrderId, int lineNo, String tenantId, String sku, int quantity, BigDecimal unitPrice) {
        this.erpOrderId = erpOrderId; this.lineNo = lineNo; this.tenantId = tenantId;
        this.sku = sku; this.quantity = quantity; this.unitPrice = unitPrice;
    }
}
