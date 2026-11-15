package com.enterprise.orchestration;

import io.temporal.activity.ActivityOptions;
import io.temporal.common.RetryOptions;
import io.temporal.failure.ApplicationFailure;
import io.temporal.workflow.ActivityStub;
import io.temporal.workflow.Workflow;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;

/**
 * Alternates the two bots until one accepts, one declines, or maxTurns is reached. The workflow, not the bots, owns the
 * protocol ({@link NegotiationRules}): every message is checked before it is recorded, so an LLM that wanders off-script
 * fails the workflow instead of steering it. The customer's limits (brief) go to the sales bot only. The result is
 * advisory; this workflow never writes to CRM or ERP. No tokens or secrets pass through here.
 */
public class BotNegotiationWorkflowImpl implements BotNegotiationWorkflow {
    private static ActivityOptions options(String queue) {
        // An LLM call can be slow; a malformed answer is retried a few times, a business-rule failure is not.
        return ActivityOptions.newBuilder().setTaskQueue(queue).setStartToCloseTimeout(Duration.ofMinutes(2))
                .setRetryOptions(RetryOptions.newBuilder()
                        .setInitialInterval(Duration.ofSeconds(2)).setBackoffCoefficient(2.0)
                        .setMaximumInterval(Duration.ofSeconds(30)).setMaximumAttempts(3)
                        .setDoNotRetry("ValidationFailed").build())
                .build();
    }

    private final ActivityStub sales = Workflow.newUntypedActivityStub(options(Contracts.CRM_QUEUE));
    private final ActivityStub fulfilment = Workflow.newUntypedActivityStub(options(Contracts.ERP_QUEUE));
    private final List<Contracts.BotMessage> history = new ArrayList<>();
    private String status = "Started";

    @Override
    public Contracts.BotNegotiationResult negotiate(Contracts.BotNegotiationStart in) {
        try {
            NegotiationRules.validateStart(in);
        } catch (NegotiationRules.ProtocolViolation e) {
            throw ApplicationFailure.newNonRetryableFailure(e.getMessage(), "ValidationFailed");
        }

        for (int turn = 1; turn <= in.maxTurns(); turn++) {
            var salesTurn = NegotiationRules.SALES.equals(NegotiationRules.actorOf(turn));
            status = salesTurn ? "WaitingSales" : "WaitingFulfilment";
            var input = new Contracts.BotTurnInput(1, in.tenantId(), in.correlationId(), in.negotiationId(), turn,
                    in.crmCustomerId(), salesTurn ? in.brief() : null, List.copyOf(history));
            var message = (salesTurn ? sales : fulfilment).execute(
                    salesTurn ? Contracts.CRM_SALES_BOT_TURN : Contracts.ERP_FULFILMENT_BOT_TURN,
                    Contracts.BotMessage.class, input);
            try {
                NegotiationRules.validate(message, turn, history);
            } catch (NegotiationRules.ProtocolViolation e) {
                status = "Failed";
                throw ApplicationFailure.newNonRetryableFailure(e.getMessage(), "ProtocolViolation");
            }
            history.add(message);
            var outcome = NegotiationRules.outcome(history, in.maxTurns());
            if (outcome != null) {
                status = outcome.status();
                return new Contracts.BotNegotiationResult(1, in.negotiationId(), outcome.status(), history.size(),
                        outcome.lines(), outcome.total(), outcome.reason(), List.copyOf(history));
            }
        }
        // outcome() always settles by turn maxTurns; kept so the compiler and a future edit stay honest.
        throw ApplicationFailure.newNonRetryableFailure("Negotiation did not settle", "ProtocolViolation");
    }

    @Override
    public String status() { return status; }

    @Override
    public List<Contracts.BotMessage> transcript() { return List.copyOf(history); }
}
