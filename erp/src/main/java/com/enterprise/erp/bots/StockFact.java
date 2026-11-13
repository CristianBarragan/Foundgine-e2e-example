package com.enterprise.erp.bots;

import java.math.BigDecimal;

/** What the ERP knows about one SKU for one tenant. available = on_hand - reserved. */
public record StockFact(String sku, boolean active, int available, BigDecimal minUnitPrice) {}
