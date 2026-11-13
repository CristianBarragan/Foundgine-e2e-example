package com.enterprise.erp.order;

import java.util.List;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface SalesOrderLineRepository extends JpaRepository<SalesOrderLine, SalesOrderLine.Key> {
    /** Quantity ordered per SKU (lines of the same SKU are summed). */
    @Query("select new com.enterprise.erp.order.SkuQuantity(l.sku, sum(l.quantity)) from SalesOrderLine l "
         + "where l.tenantId = :tenant and l.erpOrderId = :order group by l.sku")
    List<SkuQuantity> orderedQuantities(@Param("tenant") String tenant, @Param("order") String order);
}
