package com.enterprise.erp.customer;

/** Workflow/activity input; mirrors contracts/schemas/customer-sync.v1.json (camelCase JSON). */
public record CustomerSync(int schemaVersion, String tenantId, int crmCustomerId, int attempt, String correlationId,
                           String kind, String name, String email, String phone, String status) {}
