package com.enterprise.erp.inventory;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.math.BigDecimal;

/** Product catalogue row (read-only here; seeded by the demo script / tests). */
@Entity
@Table(name = "erp_products")
@IdClass(StockKey.class)
public class ErpProduct {
    @Id @Column(name = "tenant_id") private String tenantId;
    @Id @Column(name = "sku") private String sku;
    @Column(name = "name") private String name;
    @Column(name = "list_price") private BigDecimal listPrice;
    @Column(name = "min_unit_price") private BigDecimal minUnitPrice;
    @Column(name = "status") private String status;

    protected ErpProduct() {}

    public String getTenantId() { return tenantId; }
    public String getSku() { return sku; }
    public String getName() { return name; }
    public BigDecimal getMinUnitPrice() { return minUnitPrice; }
    public String getStatus() { return status; }
}
