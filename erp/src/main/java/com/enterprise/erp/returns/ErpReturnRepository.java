package com.enterprise.erp.returns;

import com.enterprise.erp.order.SkuQuantity;
import jakarta.persistence.LockModeType;
import java.util.List;
import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface ErpReturnRepository extends JpaRepository<ErpReturn, String> {
    Optional<ErpReturn> findByTenantIdAndCrmReturnId(String tenantId, int crmReturnId);

    /** SELECT ... FOR UPDATE: completing a return is serialised per return. */
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("select r from ErpReturn r where r.tenantId = :tenant and r.erpReturnId = :id")
    Optional<ErpReturn> lockByTenantAndId(@Param("tenant") String tenant, @Param("id") String id);

    @Query(value = "select nextval('erp_return_seq')", nativeQuery = true)
    long nextReturnNumber();

    /**
     * Insert-if-absent on (tenant, CRM return id), kept native like the other inserts that can race: a JPA save() that loses
     * would raise a unique violation and abort the transaction. Returns rows inserted.
     */
    @Modifying(flushAutomatically = true)
    @Query(value = """
            INSERT INTO erp_returns (erp_return_id, tenant_id, crm_return_id, erp_order_id, status, reason, note, correlation_id)
            VALUES (:id, :tenant, :crm, :order, :status, :reason, :note, :corr)
            ON CONFLICT (tenant_id, crm_return_id) DO NOTHING""", nativeQuery = true)
    int insertIfAbsent(@Param("id") String id, @Param("tenant") String tenant, @Param("crm") int crm,
                       @Param("order") String order, @Param("status") String status, @Param("reason") String reason,
                       @Param("note") String note, @Param("corr") String corr);

    /** Quantity per SKU already on Authorized or Completed returns of the order (rejected returns do not count). */
    @Query("select new com.enterprise.erp.order.SkuQuantity(l.sku, sum(l.quantity)) from ErpReturnLine l "
         + "join ErpReturn r on r.erpReturnId = l.erpReturnId "
         + "where r.tenantId = :tenant and r.erpOrderId = :order and r.status in ('Authorized', 'Completed') group by l.sku")
    List<SkuQuantity> returnedQuantities(@Param("tenant") String tenant, @Param("order") String order);
}
