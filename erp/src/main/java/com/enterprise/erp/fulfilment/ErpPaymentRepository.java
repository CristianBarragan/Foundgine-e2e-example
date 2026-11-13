package com.enterprise.erp.fulfilment;

import jakarta.persistence.LockModeType;
import java.math.BigDecimal;
import java.util.Collection;
import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface ErpPaymentRepository extends JpaRepository<ErpPayment, String> {
    Optional<ErpPayment> findByTenantIdAndIdempotencyKey(String tenantId, String idempotencyKey);

    /** SELECT ... FOR UPDATE: status changes on one payment are serialised. */
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("select p from ErpPayment p where p.tenantId = :tenant and p.erpPaymentId = :id")
    Optional<ErpPayment> lockByTenantAndId(@Param("tenant") String tenant, @Param("id") String id);

    /** Sum of the invoice's payments in the given statuses; null when there are none (callers treat that as zero). */
    @Query("select sum(p.amount) from ErpPayment p where p.tenantId = :tenant and p.erpInvoiceId = :invoice and p.status in :statuses")
    BigDecimal sumAmount(@Param("tenant") String tenant, @Param("invoice") String invoice, @Param("statuses") Collection<String> statuses);
}
