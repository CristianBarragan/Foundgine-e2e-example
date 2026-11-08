package com.enterprise.erp.order;

import java.math.BigDecimal;
import java.util.List;

/** Activity input; mirrors contracts/schemas/quote-accepted.v1.json (camelCase JSON). */
public record OrderRequest(int schemaVersion, String tenantId, int quoteId, int opportunityId, int crmCustomerId,
                           String erpCustomerId, String correlationId, BigDecimal total, List<Line> lines) {
    public record Line(String sku, String description, int quantity, BigDecimal unitPrice) {}
}
