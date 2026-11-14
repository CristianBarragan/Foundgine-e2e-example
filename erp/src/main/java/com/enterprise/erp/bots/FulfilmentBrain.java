package com.enterprise.erp.bots;

import java.util.List;

/** Decides the fulfilment side's reply. Implementations: rules (default, offline) and LangChain4j (LLM). */
public interface FulfilmentBrain {
    /** {@code offer} is the lines of the last sales message; {@code history} is the whole conversation so far. */
    FulfilmentGuardrail.Verdict decide(String tenantId, List<BotLine> offer, List<BotMessage> history);
}
