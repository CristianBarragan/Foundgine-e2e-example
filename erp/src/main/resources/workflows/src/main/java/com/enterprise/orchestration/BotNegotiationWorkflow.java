package com.enterprise.orchestration;

import io.temporal.workflow.QueryMethod;
import io.temporal.workflow.WorkflowInterface;
import io.temporal.workflow.WorkflowMethod;
import java.util.List;

/**
 * Relays turns between the C# sales bot (Microsoft Agent Framework, activity crm.sales-bot.take-turn on queue "crm") and the
 * Java fulfilment bot (LangChain4j, activity erp.fulfilment-bot.take-turn on queue "erp"). Id: BotNegotiation:{tenant}:{negotiationId}.
 */
@WorkflowInterface
public interface BotNegotiationWorkflow {
    @WorkflowMethod(name = "BotNegotiationWorkflow")
    Contracts.BotNegotiationResult negotiate(Contracts.BotNegotiationStart input);

    /** Waiting(Sales|Fulfilment) -> Agreed | Declined | Exhausted. */
    @QueryMethod
    String status();

    /** Messages so far, oldest first. */
    @QueryMethod
    List<Contracts.BotMessage> transcript();
}
