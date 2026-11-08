package com.enterprise.erp.returns;

/** completed = false when the return was already completed (idempotent; stock is added once). */
public record CompleteReturnResult(String erpReturnId, boolean completed) {}
