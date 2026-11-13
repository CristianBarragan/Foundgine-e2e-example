package com.enterprise.erp.inventory;

import java.io.Serializable;
import java.util.Objects;

/** Composite key (tenant, SKU) shared by erp_products and erp_inventory. */
public class StockKey implements Serializable {
    private String tenantId;
    private String sku;

    public StockKey() {}
    public StockKey(String tenantId, String sku) { this.tenantId = tenantId; this.sku = sku; }

    @Override public boolean equals(Object o) {
        return o instanceof StockKey k && Objects.equals(tenantId, k.tenantId) && Objects.equals(sku, k.sku);
    }
    @Override public int hashCode() { return Objects.hash(tenantId, sku); }
}
