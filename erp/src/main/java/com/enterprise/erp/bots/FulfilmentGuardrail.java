package com.enterprise.erp.bots;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * The hard ERP rules of the negotiation, as pure functions. They are the whole decision in rules mode and the safety net in
 * LLM mode: the model may propose, but nothing it says can commit the ERP to stock it does not have or a price below the floor.
 */
public final class FulfilmentGuardrail {
    private FulfilmentGuardrail() {}

    /** What the ERP is prepared to do with an offer. */
    public record Verdict(String intent, List<BotLine> lines, String message) {}

    /** Best answer to {@code offer}: Accept as-is, Counter with the closest feasible lines, or Decline. */
    public static Verdict evaluate(List<BotLine> offer, Map<String, StockFact> facts) {
        var feasible = new ArrayList<BotLine>();
        var notes = new ArrayList<String>();
        for (var l : offer) {
            var f = facts.get(l.sku());
            if (f == null) { notes.add(l.sku() + " is not in the catalogue"); continue; }
            if (!f.active()) { notes.add(l.sku() + " is discontinued"); continue; }
            if (f.available() < 1) { notes.add(l.sku() + " is out of stock"); continue; }
            var qty = Math.min(l.quantity(), f.available());
            var price = l.unitPrice().max(f.minUnitPrice());
            if (qty < l.quantity()) notes.add(l.sku() + ": only " + f.available() + " available");
            if (price.compareTo(l.unitPrice()) > 0) notes.add(l.sku() + ": minimum unit price is " + f.minUnitPrice().toPlainString());
            feasible.add(new BotLine(l.sku(), qty, price));
        }
        if (feasible.isEmpty()) return new Verdict(BotMessage.DECLINE, List.of(), join("Cannot supply any requested item", notes));
        if (sameLines(feasible, offer)) return new Verdict(BotMessage.ACCEPT, offer, "Stock and pricing check out.");
        return new Verdict(BotMessage.COUNTER, feasible, join("Counter-offer", notes));
    }

    /** True when every line can be honoured exactly: known, active, enough stock, price at or above the floor. */
    public static boolean feasible(List<BotLine> lines, Map<String, StockFact> facts) {
        if (lines.isEmpty()) return false;
        for (var l : lines) {
            var f = facts.get(l.sku());
            if (f == null || !f.active() || l.quantity() < 1 || l.quantity() > f.available()
                    || l.unitPrice() == null || l.unitPrice().compareTo(f.minUnitPrice()) < 0) return false;
        }
        return true;
    }

    /**
     * Applies the guardrail to a model's proposed reply. A Decline is always allowed (declining never over-commits).
     * Accept must repeat the offer and be feasible; Counter must be non-empty and feasible. Anything else is replaced by
     * the rules' own verdict.
     */
    public static Verdict enforce(Verdict proposed, List<BotLine> offer, Map<String, StockFact> facts) {
        if (proposed == null || proposed.intent() == null) return evaluate(offer, facts);
        switch (proposed.intent()) {
            case BotMessage.DECLINE:
                return new Verdict(BotMessage.DECLINE, List.of(), proposed.message());
            case BotMessage.ACCEPT:
                if (sameLines(proposed.lines(), offer) && feasible(offer, facts)) return proposed;
                break;
            case BotMessage.COUNTER:
                if (feasible(proposed.lines(), facts) && !sameLines(proposed.lines(), offer)) return proposed;
                break;
            default:
                break;
        }
        var fallback = evaluate(offer, facts);
        return new Verdict(fallback.intent(), fallback.lines(), "[rules applied] " + fallback.message());
    }

    /** Same SKUs, quantities and prices in the same order; prices compare by value (90.0 equals 90.00). */
    static boolean sameLines(List<BotLine> a, List<BotLine> b) {
        if (a.size() != b.size()) return false;
        for (int i = 0; i < a.size(); i++) {
            var x = a.get(i);
            var y = b.get(i);
            if (!x.sku().equals(y.sku()) || x.quantity() != y.quantity() || x.unitPrice().compareTo(y.unitPrice()) != 0) return false;
        }
        return true;
    }

    private static String join(String head, List<String> notes) {
        return notes.isEmpty() ? head : head + ": " + String.join("; ", notes) + ".";
    }

}
