package com.enterprise.erp.returns;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.util.Objects;

/** Quantity of one SKU on a return (lines of the same SKU are summed before they are stored). */
@Entity
@Table(name = "erp_return_lines")
@IdClass(ErpReturnLine.Key.class)
public class ErpReturnLine {
    public static class Key implements Serializable {
        private String erpReturnId;
        private String sku;
        public Key() {}
        public Key(String erpReturnId, String sku) { this.erpReturnId = erpReturnId; this.sku = sku; }
        @Override public boolean equals(Object o) { return o instanceof Key k && Objects.equals(erpReturnId, k.erpReturnId) && Objects.equals(sku, k.sku); }
        @Override public int hashCode() { return Objects.hash(erpReturnId, sku); }
    }

    @Id @Column(name = "erp_return_id") private String erpReturnId;
    @Id @Column(name = "sku") private String sku;
    @Column(name = "quantity") private int quantity;

    protected ErpReturnLine() {}

    public ErpReturnLine(String erpReturnId, String sku, int quantity) {
        this.erpReturnId = erpReturnId; this.sku = sku; this.quantity = quantity;
    }

    public String getSku() { return sku; }
    public int getQuantity() { return quantity; }
}
