package com.enterprise.erp.inventory;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;

/** One row per product: available = on_hand - reserved. Changed only through the repository's conditional updates. */
@Entity
@Table(name = "erp_inventory")
@IdClass(StockKey.class)
public class ErpInventory {
    @Id @Column(name = "tenant_id") private String tenantId;
    @Id @Column(name = "sku") private String sku;
    @Column(name = "on_hand") private int onHand;
    @Column(name = "reserved") private int reserved;

    protected ErpInventory() {}

    public String getTenantId() { return tenantId; }
    public String getSku() { return sku; }
    public int getOnHand() { return onHand; }
    public int getReserved() { return reserved; }
}
