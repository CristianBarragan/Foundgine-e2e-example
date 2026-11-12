package com.enterprise.erp.order;

/** cancelled = false when the order was already cancelled (the call is idempotent). */
public record CancelOrderResult(String erpOrderId, boolean cancelled) {}
