package com.enterprise.orchestration;

import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;

/** Client-side view of the ERP activities (implemented in the ERP service). */
@ActivityInterface
public interface ErpCustomerActivities {
    @ActivityMethod(name = Contracts.ERP_VALIDATE_AND_CREATE)
    Contracts.ErpCustomerResult validateAndCreateCustomer(Contracts.CustomerSync input);
}
