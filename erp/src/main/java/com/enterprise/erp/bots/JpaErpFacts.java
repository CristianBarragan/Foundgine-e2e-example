package com.enterprise.erp.bots;

import com.enterprise.erp.inventory.ErpInventoryService;
import com.enterprise.erp.inventory.ErpProductRepository;
import com.enterprise.erp.inventory.StockKey;
import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.Map;
import org.springframework.stereotype.Component;

/** {@link ErpFacts} over the existing ERP services (stock view and product price floor). */
@Component
public class JpaErpFacts implements ErpFacts {
    private final ErpInventoryService inventory;
    private final ErpProductRepository products;

    public JpaErpFacts(ErpInventoryService inventory, ErpProductRepository products) {
        this.inventory = inventory;
        this.products = products;
    }

    @Override
    public Map<String, StockFact> factsFor(String tenantId, Collection<String> skus) {
        var result = new LinkedHashMap<String, StockFact>();
        for (var sku : skus) {
            var product = products.findById(new StockKey(tenantId, sku));
            if (product.isEmpty()) continue;
            var p = product.get();
            var available = inventory.find(tenantId, sku).map(ErpInventoryService.StockView::available).orElse(0);
            result.put(sku, new StockFact(sku, "Active".equals(p.getStatus()), available, p.getMinUnitPrice()));
        }
        return result;
    }
}
