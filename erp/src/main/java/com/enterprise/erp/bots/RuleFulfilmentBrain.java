package com.enterprise.erp.bots;

import java.util.List;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Component;

/** Deterministic fulfilment bot: the guardrail is the answer. Default, so demos and CI need no model or API key. */
@Component
@ConditionalOnProperty(name = "bots.mode", havingValue = "rules", matchIfMissing = true)
public class RuleFulfilmentBrain implements FulfilmentBrain {
    private final ErpFacts facts;

    public RuleFulfilmentBrain(ErpFacts facts) { this.facts = facts; }

    @Override
    public FulfilmentGuardrail.Verdict decide(String tenantId, List<BotLine> offer, List<BotMessage> history) {
        var skus = offer.stream().map(BotLine::sku).distinct().toList();
        return FulfilmentGuardrail.evaluate(offer, facts.factsFor(tenantId, skus));
    }
}
