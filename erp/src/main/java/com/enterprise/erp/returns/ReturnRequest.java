package com.enterprise.erp.returns;

import java.util.List;

/** Activity input; mirrors contracts/schemas/return-requested.v1.json (camelCase JSON). */
public record ReturnRequest(int schemaVersion, String tenantId, int returnId, int quoteId, int crmCustomerId,
                            String erpCustomerId, String erpOrderId, String correlationId, String reason, List<Line> lines) {
    public record Line(String sku, int quantity) {}
}
