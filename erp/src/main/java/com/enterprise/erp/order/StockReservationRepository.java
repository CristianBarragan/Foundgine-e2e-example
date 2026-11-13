package com.enterprise.erp.order;

import java.util.List;
import org.springframework.data.jpa.repository.JpaRepository;

public interface StockReservationRepository extends JpaRepository<StockReservation, StockReservation.Key> {
    /** Reservations still holding stock: neither released by a cancellation nor consumed by shipping. */
    List<StockReservation> findByErpOrderIdAndReleasedFalseAndConsumedFalseOrderBySku(String erpOrderId);
}
