package com.enterprise.orchestration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;

import java.math.BigDecimal;
import java.util.List;
import org.junit.jupiter.api.Test;

class NegotiationRulesTest {
    static Contracts.BotLine line(String sku, int qty, String price) {
        return new Contracts.BotLine(sku, qty, new BigDecimal(price));
    }

    static Contracts.BotMessage msg(String from, int turn, String intent, Contracts.BotLine... lines) {
        return new Contracts.BotMessage(1, from, turn, intent, List.of(lines), "");
    }

    static final Contracts.BotMessage PROPOSE = msg("sales", 1, "Propose", line("SKU-1", 120, "85.00"));
    static final Contracts.BotMessage COUNTER = msg("fulfilment", 2, "Counter", line("SKU-1", 100, "90.00"));

    @Test void sales_takes_odd_turns_and_fulfilment_even_turns() {
        assertEquals("sales", NegotiationRules.actorOf(1));
        assertEquals("fulfilment", NegotiationRules.actorOf(2));
        assertEquals("sales", NegotiationRules.actorOf(3));
    }

    @Test void a_well_formed_conversation_passes_validation() {
        NegotiationRules.validate(PROPOSE, 1, List.of());
        NegotiationRules.validate(COUNTER, 2, List.of(PROPOSE));
        NegotiationRules.validate(msg("sales", 3, "Accept", line("SKU-1", 100, "90.00")), 3, List.of(PROPOSE, COUNTER));
        NegotiationRules.validate(msg("fulfilment", 2, "Decline"), 2, List.of(PROPOSE));
    }

    @Test void the_wrong_speaker_or_turn_number_is_rejected() {
        assertThrows(NegotiationRules.ProtocolViolation.class,
                () -> NegotiationRules.validate(msg("fulfilment", 1, "Propose", line("SKU-1", 1, "1")), 1, List.of()));
        assertThrows(NegotiationRules.ProtocolViolation.class,
                () -> NegotiationRules.validate(msg("fulfilment", 3, "Counter", line("SKU-1", 1, "1")), 2, List.of(PROPOSE)));
    }

    @Test void only_turn_one_may_propose_and_turn_one_must() {
        assertThrows(NegotiationRules.ProtocolViolation.class,
                () -> NegotiationRules.validate(msg("fulfilment", 2, "Propose", line("SKU-1", 1, "1")), 2, List.of(PROPOSE)));
        assertThrows(NegotiationRules.ProtocolViolation.class,
                () -> NegotiationRules.validate(msg("sales", 1, "Accept", line("SKU-1", 1, "1")), 1, List.of()));
    }

    @Test void accept_must_repeat_the_previous_lines_exactly() {
        assertThrows(NegotiationRules.ProtocolViolation.class, () -> NegotiationRules.validate(
                msg("sales", 3, "Accept", line("SKU-1", 100, "80.00")), 3, List.of(PROPOSE, COUNTER)));
    }

    @Test void accept_compares_prices_by_value_not_scale() {
        NegotiationRules.validate(msg("sales", 3, "Accept", line("SKU-1", 100, "90.0")), 3, List.of(PROPOSE, COUNTER));
    }

    @Test void decline_carries_no_lines_and_offers_need_valid_lines() {
        assertThrows(NegotiationRules.ProtocolViolation.class,
                () -> NegotiationRules.validate(msg("fulfilment", 2, "Decline", line("SKU-1", 1, "1")), 2, List.of(PROPOSE)));
        assertThrows(NegotiationRules.ProtocolViolation.class,
                () -> NegotiationRules.validate(msg("fulfilment", 2, "Counter"), 2, List.of(PROPOSE)));
        assertThrows(NegotiationRules.ProtocolViolation.class, () -> NegotiationRules.validate(
                msg("fulfilment", 2, "Counter", line("SKU-1", 1, "1"), line("SKU-1", 2, "1")), 2, List.of(PROPOSE)));
        assertThrows(NegotiationRules.ProtocolViolation.class,
                () -> NegotiationRules.validate(msg("fulfilment", 2, "Counter", line("SKU-1", 0, "1")), 2, List.of(PROPOSE)));
        assertThrows(NegotiationRules.ProtocolViolation.class,
                () -> NegotiationRules.validate(msg("fulfilment", 2, "Counter", line("SKU-1", 1, "-1")), 2, List.of(PROPOSE)));
    }

    @Test void unknown_intents_are_rejected() {
        assertThrows(NegotiationRules.ProtocolViolation.class,
                () -> NegotiationRules.validate(msg("fulfilment", 2, "Shrug", line("SKU-1", 1, "1")), 2, List.of(PROPOSE)));
    }

    @Test void the_conversation_continues_until_accept_decline_or_the_turn_limit() {
        assertNull(NegotiationRules.outcome(List.of(PROPOSE), 6));
        assertNull(NegotiationRules.outcome(List.of(PROPOSE, COUNTER), 6));

        var accept = msg("sales", 3, "Accept", line("SKU-1", 100, "90.00"));
        var agreed = NegotiationRules.outcome(List.of(PROPOSE, COUNTER, accept), 6);
        assertEquals("Agreed", agreed.status());
        assertEquals(new BigDecimal("9000.00"), agreed.total());
        assertEquals(1, agreed.lines().size());

        var declined = NegotiationRules.outcome(List.of(PROPOSE, new Contracts.BotMessage(1, "fulfilment", 2, "Decline",
                List.of(), "SKU-1 is discontinued")), 6);
        assertEquals("Declined", declined.status());
        assertEquals("SKU-1 is discontinued", declined.reason());
        assertEquals(BigDecimal.ZERO, declined.total());

        var exhausted = NegotiationRules.outcome(List.of(PROPOSE, COUNTER), 2);
        assertNotNull(exhausted);
        assertEquals("Exhausted", exhausted.status());
    }

    @Test void start_input_is_validated() {
        var ok = new Contracts.BotNegotiationStart(1, "acme", "c", "n1", 42, 6, new Contracts.Brief("",
                List.of(new Contracts.BriefLine("SKU-1", 120, 80, new BigDecimal("95")))));
        NegotiationRules.validateStart(ok);
        assertThrows(NegotiationRules.ProtocolViolation.class, () -> NegotiationRules.validateStart(
                new Contracts.BotNegotiationStart(1, "acme", "c", "n1", 42, 1, ok.brief())));
        assertThrows(NegotiationRules.ProtocolViolation.class, () -> NegotiationRules.validateStart(
                new Contracts.BotNegotiationStart(1, "acme", "c", "n1", 42, 6, new Contracts.Brief("",
                        List.of(new Contracts.BriefLine("SKU-1", 10, 20, new BigDecimal("95")))))));
        assertThrows(NegotiationRules.ProtocolViolation.class, () -> NegotiationRules.validateStart(
                new Contracts.BotNegotiationStart(1, "", "c", "n1", 42, 6, ok.brief())));
    }
}
