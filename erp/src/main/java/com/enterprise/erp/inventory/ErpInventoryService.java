package com.enterprise.erp.inventory;

import java.util.HashMap;
import java.util.List;
import java.util.Optional;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/** Read-only view of stock for one tenant. available = on_hand - reserved (the CHECK constraints keep it >= 0). */
@Service
public class ErpInventoryService {
    private final ErpInventoryRepository inventory;
    private final ErpProductRepository products;

    public ErpInventoryService(ErpInventoryRepository inventory, ErpProductRepository products) {
        this.inventory = inventory;
        this.products = products;
    }

    public record StockView(String sku, String name, int onHand, int reserved, int available) {}

    @Transactional(readOnly = true)
    public List<StockView> list(String tenantId) {
        var rows = inventory.findByTenantIdOrderBySku(tenantId);
        var names = new HashMap<String, String>();
        products.findByTenantIdAndSkuIn(tenantId, rows.stream().map(ErpInventory::getSku).toList())
                .forEach(p -> names.put(p.getSku(), p.getName()));
        return rows.stream().map(i -> view(i, names.get(i.getSku()))).toList();
    }

    @Transactional(readOnly = true)
    public Optional<StockView> find(String tenantId, String sku) {
        return inventory.findByTenantIdAndSku(tenantId, sku).map(i -> view(i,
                products.findById(new StockKey(tenantId, sku)).map(ErpProduct::getName).orElse(sku)));
    }

    private static StockView view(ErpInventory i, String name) {
        return new StockView(i.getSku(), name, i.getOnHand(), i.getReserved(), i.getOnHand() - i.getReserved());
    }
}
