package com.enterprise.erp.bots;

import java.util.Collection;
import java.util.Map;

/** Read-only source of SKU facts. The tenant always comes from the workflow input, never from the model. */
public interface ErpFacts {
    /** SKUs the ERP does not know are simply absent from the result. */
    Map<String, StockFact> factsFor(String tenantId, Collection<String> skus);
}
