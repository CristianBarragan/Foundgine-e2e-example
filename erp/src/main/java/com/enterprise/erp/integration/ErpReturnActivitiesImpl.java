package com.enterprise.erp.integration;

import com.enterprise.erp.customer.ValidationFailedException;
import com.enterprise.erp.returns.CompleteReturnRequest;
import com.enterprise.erp.returns.CompleteReturnResult;
import com.enterprise.erp.returns.ErpReturnDecision;
import com.enterprise.erp.returns.ErpReturnService;
import com.enterprise.erp.returns.ReturnRequest;
import io.temporal.failure.ApplicationFailure;
import org.springframework.stereotype.Component;

@Component
public class ErpReturnActivitiesImpl implements ErpReturnActivities {
    public static final String VALIDATION_FAILED = "ValidationFailed";
    private final ErpReturnService service;

    public ErpReturnActivitiesImpl(ErpReturnService service) { this.service = service; }

    @Override
    public ErpReturnDecision authorizeReturn(ReturnRequest input) {
        try {
            return service.authorize(input);
        } catch (ValidationFailedException e) {
            throw ApplicationFailure.newNonRetryableFailure(e.getMessage(), VALIDATION_FAILED);
        }
    }

    @Override
    public CompleteReturnResult completeReturn(CompleteReturnRequest input) {
        try {
            return service.complete(input);
        } catch (ValidationFailedException e) {
            throw ApplicationFailure.newNonRetryableFailure(e.getMessage(), VALIDATION_FAILED);
        }
    }
}
