package com.enterprise.erp.bots;

import java.util.List;

/** One turn of the negotiation; mirrors contracts/schemas/bot-message.v1.json. from: sales | fulfilment. */
public record BotMessage(int schemaVersion, String from, int turn, String intent, List<BotLine> lines, String message) {
    public static final String PROPOSE = "Propose";
    public static final String COUNTER = "Counter";
    public static final String ACCEPT = "Accept";
    public static final String DECLINE = "Decline";
}
