package com.enterprise.erp.inventory;

import java.util.Collection;
import java.util.List;
import org.springframework.data.jpa.repository.JpaRepository;

public interface ErpProductRepository extends JpaRepository<ErpProduct, StockKey> {
    List<ErpProduct> findByTenantIdAndSkuIn(String tenantId, Collection<String> skus);

    List<ErpProduct> findByTenantIdAndStatusAndSkuIn(String tenantId, String status, Collection<String> skus);
}
