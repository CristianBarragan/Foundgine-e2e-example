package com.enterprise.erp.bots;

import java.util.List;

/**
 * Activity input; mirrors contracts/schemas/bot-turn-input.v1.json. The customer's brief (limits) is deliberately NOT a
 * field here: the fulfilment bot must never see what the customer is willing to pay, so it is dropped on deserialisation.
 */
public record BotTurnInput(int schemaVersion, String tenantId, String correlationId, String negotiationId, int turn,
                           int crmCustomerId, List<BotMessage> history) {}
