package com.enterprise.erp.bots;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.math.BigDecimal;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class FulfilmentGuardrailTest {
    static BotLine line(String sku, int qty, String price) { return new BotLine(sku, qty, new BigDecimal(price)); }

    static final Map<String, StockFact> FACTS = Map.of(
            "SKU-1", new StockFact("SKU-1", true, 100, new BigDecimal("90.00")),
            "OLD", new StockFact("OLD", false, 500, new BigDecimal("1.00")),
            "OUT", new StockFact("OUT", true, 0, new BigDecimal("1.00")));

    @Test void an_offer_the_erp_can_honour_is_accepted_as_is() {
        var offer = List.of(line("SKU-1", 100, "90.00"));
        var v = FulfilmentGuardrail.evaluate(offer, FACTS);
        assertEquals("Accept", v.intent());
        assertEquals(offer, v.lines());
    }

    @Test void prices_compare_by_value_not_scale() {
        var v = FulfilmentGuardrail.evaluate(List.of(line("SKU-1", 100, "90.0")), FACTS);
        assertEquals("Accept", v.intent());
    }

    @Test void too_much_stock_or_too_low_a_price_becomes_a_counter() {
        var v = FulfilmentGuardrail.evaluate(List.of(line("SKU-1", 120, "85.00")), FACTS);
        assertEquals("Counter", v.intent());
        assertEquals(List.of(line("SKU-1", 100, "90.00")), v.lines());
        assertTrue(v.message().contains("only 100 available"));
        assertTrue(v.message().contains("minimum unit price is 90.00"));
    }

    @Test void a_higher_price_than_the_floor_is_never_lowered() {
        var v = FulfilmentGuardrail.evaluate(List.of(line("SKU-1", 150, "99.00")), FACTS);
        assertEquals(List.of(line("SKU-1", 100, "99.00")), v.lines());
    }

    @Test void unsupplyable_lines_are_dropped_and_nothing_left_means_decline() {
        var mixed = FulfilmentGuardrail.evaluate(List.of(line("SKU-1", 10, "95.00"), line("OLD", 5, "5.00"), line("NOPE", 1, "1.00")), FACTS);
        assertEquals("Counter", mixed.intent());
        assertEquals(List.of(line("SKU-1", 10, "95.00")), mixed.lines());

        var none = FulfilmentGuardrail.evaluate(List.of(line("OLD", 5, "5.00"), line("OUT", 1, "1.00")), FACTS);
        assertEquals("Decline", none.intent());
        assertTrue(none.lines().isEmpty());
    }

    @Test void the_model_cannot_accept_what_the_erp_cannot_honour() {
        var offer = List.of(line("SKU-1", 120, "85.00"));
        var greedy = new FulfilmentGuardrail.Verdict("Accept", offer, "sure!");
        var v = FulfilmentGuardrail.enforce(greedy, offer, FACTS);
        assertEquals("Counter", v.intent());
        assertEquals(List.of(line("SKU-1", 100, "90.00")), v.lines());
        assertTrue(v.message().startsWith("[rules applied]"));
    }

    @Test void the_model_cannot_counter_with_stock_or_a_price_the_erp_does_not_have() {
        var offer = List.of(line("SKU-1", 120, "85.00"));
        var cheap = new FulfilmentGuardrail.Verdict("Counter", List.of(line("SKU-1", 100, "80.00")), "deal");
        assertEquals(List.of(line("SKU-1", 100, "90.00")), FulfilmentGuardrail.enforce(cheap, offer, FACTS).lines());
        var many = new FulfilmentGuardrail.Verdict("Counter", List.of(line("SKU-1", 110, "95.00")), "deal");
        assertEquals(List.of(line("SKU-1", 100, "90.00")), FulfilmentGuardrail.enforce(many, offer, FACTS).lines());
    }

    @Test void a_sound_model_answer_passes_through_and_a_decline_is_always_allowed() {
        var offer = List.of(line("SKU-1", 120, "85.00"));
        var fine = new FulfilmentGuardrail.Verdict("Counter", List.of(line("SKU-1", 60, "92.00")), "smaller deal");
        assertEquals(fine, FulfilmentGuardrail.enforce(fine, offer, FACTS));
        var no = new FulfilmentGuardrail.Verdict("Decline", List.of(line("SKU-1", 1, "1")), "not today");
        var v = FulfilmentGuardrail.enforce(no, offer, FACTS);
        assertEquals("Decline", v.intent());
        assertTrue(v.lines().isEmpty());
    }

    @Test void nonsense_from_the_model_falls_back_to_the_rules() {
        var offer = List.of(line("SKU-1", 10, "95.00"));
        assertEquals("Accept", FulfilmentGuardrail.enforce(new FulfilmentGuardrail.Verdict("Shrug", List.of(), ""), offer, FACTS).intent());
        assertEquals("Accept", FulfilmentGuardrail.enforce(null, offer, FACTS).intent());
    }
}
