package com.enterprise.erp.integration;

import com.enterprise.erp.customer.CustomerSync;
import com.enterprise.erp.customer.ErpCustomerResult;
import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;

/** ERP-owned activities, executed on the "erp" task queue. Names are the cross-language contract. */
@ActivityInterface
public interface ErpCustomerActivities {
    String VALIDATE_AND_CREATE_CUSTOMER = "erp.validate-and-create-customer";

    /** Validation failures are non-retryable (type "ValidationFailed"); everything else is retried by the workflow's policy. */
    @ActivityMethod(name = VALIDATE_AND_CREATE_CUSTOMER)
    ErpCustomerResult validateAndCreateCustomer(CustomerSync input);
}
