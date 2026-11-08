package com.enterprise.erp.order;

/** created = false when the order already existed for this (tenant, quote) and nothing new was reserved. */
public record ErpOrderResult(String erpOrderId, boolean created) {}
