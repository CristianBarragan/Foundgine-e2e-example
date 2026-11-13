package com.enterprise.erp.fulfilment;

import jakarta.persistence.LockModeType;
import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

public interface ErpShipmentRepository extends JpaRepository<ErpShipment, String> {
    Optional<ErpShipment> findByTenantIdAndIdempotencyKey(String tenantId, String idempotencyKey);

    /** SELECT ... FOR UPDATE: status transitions on one shipment are serialised. */
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("select s from ErpShipment s where s.tenantId = :tenant and s.erpShipmentId = :id")
    Optional<ErpShipment> lockByTenantAndId(@Param("tenant") String tenant, @Param("id") String id);
}
