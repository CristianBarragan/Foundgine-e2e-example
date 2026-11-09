package com.foundgine.samples.supplychain.advanced.integration;

import com.fasterxml.jackson.annotation.JsonProperty;

import java.math.BigDecimal;
import java.util.List;

/** Wire contracts for /api/integration/*. Field names match the orchestrator's C# records (camelCase). */
public final class IntegrationModels {
    private IntegrationModels() {}

    /** productId carries the ERP SKU (products.sku). unitOfMeasure is accepted but not modelled by this ERP. */
    public record Line(String productId, BigDecimal quantity, String unitOfMeasure) {}

    public record CreateOrder(
            String tenantId,
            String crmCommercialOrderId,
            String orderNumber,
            String customerId,
            String currency,
            List<Line> lines,
            String idempotencyKey) {}

    public record Reserve(
            String tenantId,
            String crmCommercialOrderId,
            String erpOrderId,
            List<Line> lines,
            String idempotencyKey) {}

    public record Compensation(
            String tenantId,
            String crmCommercialOrderId,
            String externalId,
            String reason,
            String idempotencyKey) {}

    public record ErpOrderResult(String erpOrderId, String status) {}

    public record ReservationResult(
            @JsonProperty("isReserved") boolean isReserved,
            String reservationId,
            String rejectionReason) {}

    public record Ack(String status) {}
}
