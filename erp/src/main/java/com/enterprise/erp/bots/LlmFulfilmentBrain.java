package com.enterprise.erp.bots;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import dev.langchain4j.model.chat.ChatModel;
import dev.langchain4j.service.AiServices;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.List;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Component;

/**
 * LangChain4j fulfilment bot (bots.mode=llm). The model reads the offer, calls the read-only ERP tool, and proposes a reply;
 * {@link FulfilmentGuardrail#enforce} then overrules anything the ERP could not honour.
 */
@Component
@ConditionalOnProperty(name = "bots.mode", havingValue = "llm")
public class LlmFulfilmentBrain implements FulfilmentBrain {
    private static final ObjectMapper JSON = new ObjectMapper();
    private final ChatModel model;
    private final ErpFacts facts;

    public LlmFulfilmentBrain(ChatModel model, ErpFacts facts) {
        this.model = model;
        this.facts = facts;
    }

    @Override
    public FulfilmentGuardrail.Verdict decide(String tenantId, List<BotLine> offer, List<BotMessage> history) {
        var assistant = AiServices.builder(FulfilmentAssistant.class)
                .chatModel(model)
                .tools(new FulfilmentTools(tenantId, facts))
                .build();
        String reply;
        try {
            reply = assistant.decide(situation(offer, history));
        } catch (RuntimeException e) {
            throw new IllegalStateException("Fulfilment model call failed: " + e.getMessage(), e); // retryable
        }
        var proposed = parse(reply); // IllegalStateException on garbage: the activity is retried
        var skus = offer.stream().map(BotLine::sku).distinct().toList();
        return FulfilmentGuardrail.enforce(proposed, offer, facts.factsFor(tenantId, skus));
    }

    private static String situation(List<BotLine> offer, List<BotMessage> history) {
        try {
            var transcript = new ArrayList<String>();
            for (var m : history) transcript.add(m.from() + " (turn " + m.turn() + ", " + m.intent() + "): " + m.message());
            return "Current offer: " + JSON.writeValueAsString(offer) + "\nConversation so far:\n" + String.join("\n", transcript);
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

    static FulfilmentGuardrail.Verdict parse(String reply) {
        try {
            var text = reply == null ? "" : reply.strip();
            var start = text.indexOf('{');
            var end = text.lastIndexOf('}');
            if (start < 0 || end < start) throw new IllegalStateException("The model did not return a JSON object");
            JsonNode node = JSON.readTree(text.substring(start, end + 1));
            var lines = new ArrayList<BotLine>();
            for (var l : node.path("lines")) {
                lines.add(new BotLine(l.path("sku").asText(), l.path("quantity").asInt(), new BigDecimal(l.path("unitPrice").asText("0"))));
            }
            var message = node.path("message").asText("");
            if (message.length() > 1000) message = message.substring(0, 1000);
            return new FulfilmentGuardrail.Verdict(node.path("intent").asText(""), List.copyOf(lines), message);
        } catch (IllegalStateException e) {
            throw e;
        } catch (Exception e) {
            throw new IllegalStateException("Unreadable model reply: " + e.getMessage(), e);
        }
    }
}
