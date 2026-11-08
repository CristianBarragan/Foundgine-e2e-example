package com.enterprise.erp.integration;

import com.enterprise.erp.customer.CustomerSync;
import com.enterprise.erp.customer.ErpCustomerResult;
import com.enterprise.erp.customer.ErpCustomerService;
import com.enterprise.erp.customer.ValidationFailedException;
import io.temporal.failure.ApplicationFailure;
import org.springframework.stereotype.Component;

@Component
public class ErpCustomerActivitiesImpl implements ErpCustomerActivities {
    public static final String VALIDATION_FAILED = "ValidationFailed";
    private final ErpCustomerService service;

    public ErpCustomerActivitiesImpl(ErpCustomerService service) { this.service = service; }

    @Override
    public ErpCustomerResult validateAndCreateCustomer(CustomerSync input) {
        try {
            return service.createOrGet(input);
        } catch (ValidationFailedException e) {
            throw ApplicationFailure.newNonRetryableFailure(e.getMessage(), VALIDATION_FAILED);
        }
    }
}
