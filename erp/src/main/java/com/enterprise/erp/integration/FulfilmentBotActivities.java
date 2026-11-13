package com.enterprise.erp.integration;

import com.enterprise.erp.bots.BotMessage;
import com.enterprise.erp.bots.BotTurnInput;
import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;

/** The Java fulfilment bot (LangChain4j) on the "erp" task queue. The name is the cross-language contract. */
@ActivityInterface
public interface FulfilmentBotActivities {
    String TAKE_TURN = "erp.fulfilment-bot.take-turn";

    /** Non-retryable "ValidationFailed" for a malformed input; model errors are retried by the workflow's policy. */
    @ActivityMethod(name = TAKE_TURN)
    BotMessage takeTurn(BotTurnInput input);
}
