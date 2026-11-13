package com.enterprise.erp.fulfilment;

import jakarta.persistence.LockModeType;
import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface ErpInvoiceRepository extends JpaRepository<ErpInvoice, String> {
    Optional<ErpInvoice> findByTenantIdAndErpOrderId(String tenantId, String erpOrderId);

    /** SELECT ... FOR UPDATE: payments and invoice status changes are serialised per invoice. */
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("select i from ErpInvoice i where i.tenantId = :tenant and i.erpInvoiceId = :id")
    Optional<ErpInvoice> lockByTenantAndId(@Param("tenant") String tenant, @Param("id") String id);
}
