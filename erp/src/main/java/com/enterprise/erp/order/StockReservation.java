package com.enterprise.erp.order;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.util.Objects;

/**
 * Reservation per order and SKU. Exactly one of three states: held (released = false, consumed = false), released
 * (the order was cancelled first) or consumed (shipping took the stock out of the warehouse). The database rejects
 * released AND consumed.
 */
@Entity
@Table(name = "stock_reservations")
@IdClass(StockReservation.Key.class)
public class StockReservation {
    public static class Key implements Serializable {
        private String erpOrderId;
        private String sku;
        public Key() {}
        public Key(String erpOrderId, String sku) { this.erpOrderId = erpOrderId; this.sku = sku; }
        @Override public boolean equals(Object o) { return o instanceof Key k && Objects.equals(erpOrderId, k.erpOrderId) && Objects.equals(sku, k.sku); }
        @Override public int hashCode() { return Objects.hash(erpOrderId, sku); }
    }

    @Id @Column(name = "erp_order_id") private String erpOrderId;
    @Id @Column(name = "sku") private String sku;
    @Column(name = "tenant_id") private String tenantId;
    @Column(name = "quantity") private int quantity;
    @Column(name = "released") private boolean released;
    @Column(name = "consumed") private boolean consumed;

    protected StockReservation() {}

    public StockReservation(String erpOrderId, String tenantId, String sku, int quantity) {
        this.erpOrderId = erpOrderId; this.tenantId = tenantId; this.sku = sku; this.quantity = quantity;
    }

    public String getSku() { return sku; }
    public int getQuantity() { return quantity; }
    public void release() { this.released = true; }
    /** Shipping took this stock out of the warehouse; a later cancellation must not release it again. */
    public void consume() { this.consumed = true; }
}
