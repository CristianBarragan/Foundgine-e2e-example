package com.enterprise.erp.inventory;

import java.util.List;
import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface ErpInventoryRepository extends JpaRepository<ErpInventory, StockKey> {
    List<ErpInventory> findByTenantIdOrderBySku(String tenantId);

    Optional<ErpInventory> findByTenantIdAndSku(String tenantId, String sku);

    /** Reserves stock with ONE conditional UPDATE, so concurrent orders can never over-reserve. Returns rows changed (0 or 1). */
    @Modifying(flushAutomatically = true)
    @Query("update ErpInventory i set i.reserved = i.reserved + :qty "
         + "where i.tenantId = :tenant and i.sku = :sku and i.onHand - i.reserved >= :qty")
    int reserve(@Param("tenant") String tenant, @Param("sku") String sku, @Param("qty") int qty);

    /** Gives back a reservation (order cancelled before it shipped). */
    @Modifying(flushAutomatically = true)
    @Query("update ErpInventory i set i.reserved = i.reserved - :qty where i.tenantId = :tenant and i.sku = :sku")
    int release(@Param("tenant") String tenant, @Param("sku") String sku, @Param("qty") int qty);

    /**
     * Shipping: the reserved stock leaves the warehouse, so on_hand and reserved both drop by the reserved quantity.
     * Returns rows changed; 0 means the inventory row no longer holds that reservation.
     */
    @Modifying(flushAutomatically = true)
    @Query("update ErpInventory i set i.onHand = i.onHand - :qty, i.reserved = i.reserved - :qty "
         + "where i.tenantId = :tenant and i.sku = :sku and i.reserved >= :qty")
    int consume(@Param("tenant") String tenant, @Param("sku") String sku, @Param("qty") int qty);

    /** A return was received: the goods are back on the shelf. Returns rows changed (0 = no inventory record for the SKU). */
    @Modifying(flushAutomatically = true)
    @Query("update ErpInventory i set i.onHand = i.onHand + :qty where i.tenantId = :tenant and i.sku = :sku")
    int restock(@Param("tenant") String tenant, @Param("sku") String sku, @Param("qty") int qty);
}
