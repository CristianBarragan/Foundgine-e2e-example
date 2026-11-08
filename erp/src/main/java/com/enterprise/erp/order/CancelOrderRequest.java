package com.enterprise.erp.order;

/** Compensation input. The order is identified by its ERP id and must belong to the tenant and quote. */
public record CancelOrderRequest(String tenantId, int quoteId, String erpOrderId, String reason) {}
