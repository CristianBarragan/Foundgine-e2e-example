package com.enterprise.erp.bots;

import com.enterprise.erp.customer.ValidationFailedException;
import java.util.List;
import org.springframework.stereotype.Service;

/** Produces the fulfilment bot's message for one even turn. Read-only: nothing is reserved, ordered or written. */
@Service
public class FulfilmentBotService {
    private final FulfilmentBrain brain;

    public FulfilmentBotService(FulfilmentBrain brain) { this.brain = brain; }

    public BotMessage takeTurn(BotTurnInput in) {
        var errors = validate(in);
        if (!errors.isEmpty()) throw new ValidationFailedException(errors);
        var last = in.history().get(in.history().size() - 1);
        var verdict = brain.decide(in.tenantId(), last.lines(), in.history());
        var text = verdict.message() == null ? "" : verdict.message();
        if (text.length() > 1000) text = text.substring(0, 1000);
        return new BotMessage(1, "fulfilment", in.turn(), verdict.intent(), verdict.lines(), text);
    }

    static List<String> validate(BotTurnInput in) {
        var errors = new java.util.ArrayList<String>();
        if (in == null) return List.of("input is required");
        if (in.schemaVersion() != 1) errors.add("unsupported schemaVersion " + in.schemaVersion());
        if (in.tenantId() == null || in.tenantId().isBlank()) errors.add("tenantId is required");
        if (in.turn() < 2 || in.turn() % 2 != 0) errors.add("fulfilment takes even turns, not " + in.turn());
        if (in.history() == null || in.history().isEmpty()) {
            errors.add("history is required");
        } else {
            var last = in.history().get(in.history().size() - 1);
            if (in.history().size() != in.turn() - 1) errors.add("history must hold exactly the " + (in.turn() - 1) + " earlier messages");
            if (!"sales".equals(last.from())) errors.add("the last message must come from sales");
            if (!BotMessage.PROPOSE.equals(last.intent()) && !BotMessage.COUNTER.equals(last.intent()))
                errors.add("nothing to answer: the last message is " + last.intent());
            if (last.lines() == null || last.lines().isEmpty()) errors.add("the last message has no lines");
        }
        return errors;
    }
}
