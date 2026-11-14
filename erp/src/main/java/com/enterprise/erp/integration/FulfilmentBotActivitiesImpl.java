package com.enterprise.erp.integration;

import com.enterprise.erp.bots.BotMessage;
import com.enterprise.erp.bots.BotTurnInput;
import com.enterprise.erp.bots.FulfilmentBotService;
import com.enterprise.erp.customer.ValidationFailedException;
import io.temporal.failure.ApplicationFailure;
import org.springframework.stereotype.Component;

@Component
public class FulfilmentBotActivitiesImpl implements FulfilmentBotActivities {
    private final FulfilmentBotService service;

    public FulfilmentBotActivitiesImpl(FulfilmentBotService service) { this.service = service; }

    @Override
    public BotMessage takeTurn(BotTurnInput input) {
        try {
            return service.takeTurn(input);
        } catch (ValidationFailedException e) {
            throw ApplicationFailure.newNonRetryableFailure(e.getMessage(), ErpOrderActivitiesImpl.VALIDATION_FAILED);
        }
    }
}
