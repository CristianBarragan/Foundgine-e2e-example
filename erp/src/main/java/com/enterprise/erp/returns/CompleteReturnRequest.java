package com.enterprise.erp.returns;

/** Goods are back in the warehouse: put the stock back. Identified by the ERP return id within the tenant. */
public record CompleteReturnRequest(String tenantId, String erpReturnId) {}
