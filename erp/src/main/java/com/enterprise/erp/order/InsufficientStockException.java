package com.enterprise.erp.order;

/** Not enough available stock for a SKU. Retrying immediately cannot help, so the activity fails it non-retryably. */
public class InsufficientStockException extends RuntimeException {
    private final String sku;

    public InsufficientStockException(String sku, int requested) {
        super("Insufficient stock for " + sku + " (requested " + requested + ").");
        this.sku = sku;
    }

    public String sku() { return sku; }
}
