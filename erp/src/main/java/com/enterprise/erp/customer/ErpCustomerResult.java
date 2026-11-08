package com.enterprise.erp.customer;

/** Output of the create activity. {@code created} is false when a duplicate message found the existing customer. */
public record ErpCustomerResult(String erpCustomerId, boolean created) {}
