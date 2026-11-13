package com.enterprise.erp.bots;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.enterprise.erp.customer.ValidationFailedException;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class FulfilmentBotServiceTest {
    static BotLine line(String sku, int qty, String price) { return new BotLine(sku, qty, new BigDecimal(price)); }

    final List<String> askedTenants = new ArrayList<>();
    final ErpFacts facts = (tenant, skus) -> {
        askedTenants.add(tenant);
        return Map.of("SKU-1", new StockFact("SKU-1", true, 100, new BigDecimal("90.00")));
    };
    final FulfilmentBotService service = new FulfilmentBotService(new RuleFulfilmentBrain(facts));

    static BotMessage propose(String intent, BotLine l) { return new BotMessage(1, "sales", 1, intent, List.of(l), "hi"); }

    static BotTurnInput input(int turn, List<BotMessage> history) {
        return new BotTurnInput(1, "acme", "corr", "neg-1", turn, 42, history);
    }

    @Test void answers_a_proposal_with_a_counter_for_the_calling_tenant_only() {
        var reply = service.takeTurn(input(2, List.of(propose("Propose", line("SKU-1", 120, "85.00")))));
        assertEquals("fulfilment", reply.from());
        assertEquals(2, reply.turn());
        assertEquals("Counter", reply.intent());
        assertEquals(List.of(line("SKU-1", 100, "90.00")), reply.lines());
        assertEquals(List.of("acme"), askedTenants);
    }

    @Test void accepts_an_offer_it_can_honour() {
        var reply = service.takeTurn(input(2, List.of(propose("Propose", line("SKU-1", 50, "95.00")))));
        assertEquals("Accept", reply.intent());
    }

    @Test void rejects_malformed_inputs_without_asking_the_erp() {
        var p = propose("Propose", line("SKU-1", 1, "100.00"));
        assertThrows(ValidationFailedException.class, () -> service.takeTurn(input(3, List.of(p)))); // odd turn
        assertThrows(ValidationFailedException.class, () -> service.takeTurn(input(2, List.of()))); // no history
        assertThrows(ValidationFailedException.class, () -> service.takeTurn(input(4, List.of(p)))); // history too short
        var fromUs = new BotMessage(1, "fulfilment", 1, "Counter", List.of(line("SKU-1", 1, "100.00")), "");
        assertThrows(ValidationFailedException.class, () -> service.takeTurn(input(2, List.of(fromUs))));
        var accept = new BotMessage(1, "sales", 1, "Accept", List.of(line("SKU-1", 1, "100.00")), "");
        assertThrows(ValidationFailedException.class, () -> service.takeTurn(input(2, List.of(accept))));
        assertTrue(askedTenants.isEmpty());
    }
}
