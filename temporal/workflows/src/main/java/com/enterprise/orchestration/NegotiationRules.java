package com.enterprise.orchestration;

import java.math.BigDecimal;
import java.util.HashSet;
import java.util.List;

/**
 * Pure protocol rules of the bot negotiation (no Temporal, no I/O), so they are unit-testable and replay-safe.
 * The workflow enforces them on every message regardless of what the bot (LLM or rules) produced.
 * Sales takes odd turns, fulfilment even turns. Turn 1 is a Propose; afterwards Counter, Accept or Decline.
 * Accept must repeat the previous message's lines exactly; Decline carries none.
 */
public final class NegotiationRules {
    private NegotiationRules() {}

    public static final String SALES = "sales";
    public static final String FULFILMENT = "fulfilment";

    /** Thrown for a message that breaks the protocol; the workflow turns it into a non-retryable failure. */
    public static final class ProtocolViolation extends RuntimeException {
        public ProtocolViolation(String message) { super(message); }
    }

    /** What the conversation has settled on; null from {@link #outcome} means "keep going". */
    public record Outcome(String status, List<Contracts.BotLine> lines, BigDecimal total, String reason) {}

    public static String actorOf(int turn) { return turn % 2 == 1 ? SALES : FULFILMENT; }

    public static void validateStart(Contracts.BotNegotiationStart in) {
        if (in.schemaVersion() != 1) throw new ProtocolViolation("Unsupported schemaVersion " + in.schemaVersion());
        if (in.tenantId() == null || in.tenantId().isBlank()) throw new ProtocolViolation("tenantId is required");
        if (in.negotiationId() == null || in.negotiationId().isBlank()) throw new ProtocolViolation("negotiationId is required");
        if (in.maxTurns() < 2 || in.maxTurns() > 10) throw new ProtocolViolation("maxTurns must be between 2 and 10");
        if (in.brief() == null || in.brief().lines() == null || in.brief().lines().isEmpty())
            throw new ProtocolViolation("The brief needs at least one line");
        for (var l : in.brief().lines()) {
            if (l.sku() == null || l.sku().isBlank() || l.quantity() < 1 || l.minQuantity() < 1
                    || l.minQuantity() > l.quantity() || l.maxUnitPrice() == null || l.maxUnitPrice().signum() < 0)
                throw new ProtocolViolation("Invalid brief line for " + l.sku());
        }
    }

    /** Checks the message a bot produced for {@code turn} against everything said so far. */
    public static void validate(Contracts.BotMessage m, int turn, List<Contracts.BotMessage> history) {
        if (m == null) throw new ProtocolViolation("Turn " + turn + ": no message");
        if (m.schemaVersion() != 1) throw new ProtocolViolation("Turn " + turn + ": unsupported schemaVersion");
        if (m.turn() != turn) throw new ProtocolViolation("Turn " + turn + ": message is numbered " + m.turn());
        if (!actorOf(turn).equals(m.from()))
            throw new ProtocolViolation("Turn " + turn + " belongs to " + actorOf(turn) + ", not " + m.from());
        if (m.message() != null && m.message().length() > 1000) throw new ProtocolViolation("Turn " + turn + ": message too long");
        var intent = m.intent() == null ? "" : m.intent();
        var lines = m.lines() == null ? List.<Contracts.BotLine>of() : m.lines();
        switch (intent) {
            case "Propose" -> {
                if (turn != 1) throw new ProtocolViolation("Turn " + turn + ": only turn 1 may Propose");
                checkOffer(lines, turn);
            }
            case "Counter" -> {
                if (turn == 1) throw new ProtocolViolation("Turn 1 must Propose");
                checkOffer(lines, turn);
            }
            case "Accept" -> {
                if (turn == 1) throw new ProtocolViolation("Turn 1 must Propose");
                var previous = history.get(history.size() - 1).lines();
                if (!sameLines(lines, previous))
                    throw new ProtocolViolation("Turn " + turn + ": Accept must repeat the lines of turn " + (turn - 1));
            }
            case "Decline" -> {
                if (turn == 1) throw new ProtocolViolation("Turn 1 must Propose");
                if (!lines.isEmpty()) throw new ProtocolViolation("Turn " + turn + ": Decline carries no lines");
            }
            default -> throw new ProtocolViolation("Turn " + turn + ": unknown intent '" + intent + "'");
        }
    }

    /** Same SKUs, quantities and prices in the same order; prices compare by value (90.0 equals 90.00). */
    static boolean sameLines(List<Contracts.BotLine> a, List<Contracts.BotLine> b) {
        if (a.size() != b.size()) return false;
        for (int i = 0; i < a.size(); i++) {
            var x = a.get(i);
            var y = b.get(i);
            if (!x.sku().equals(y.sku()) || x.quantity() != y.quantity() || x.unitPrice().compareTo(y.unitPrice()) != 0) return false;
        }
        return true;
    }

    private static void checkOffer(List<Contracts.BotLine> lines, int turn) {
        if (lines.isEmpty()) throw new ProtocolViolation("Turn " + turn + ": an offer needs at least one line");
        var seen = new HashSet<String>();
        for (var l : lines) {
            if (l.sku() == null || l.sku().isBlank()) throw new ProtocolViolation("Turn " + turn + ": line without sku");
            if (!seen.add(l.sku())) throw new ProtocolViolation("Turn " + turn + ": duplicate sku " + l.sku());
            if (l.quantity() < 1) throw new ProtocolViolation("Turn " + turn + ": quantity must be at least 1 for " + l.sku());
            if (l.unitPrice() == null || l.unitPrice().signum() < 0)
                throw new ProtocolViolation("Turn " + turn + ": invalid unit price for " + l.sku());
        }
    }

    /** Settles the conversation after the last message in {@code history}, or returns null to continue. */
    public static Outcome outcome(List<Contracts.BotMessage> history, int maxTurns) {
        var last = history.get(history.size() - 1);
        switch (last.intent()) {
            case "Accept" -> {
                return new Outcome("Agreed", last.lines(), total(last.lines()), "");
            }
            case "Decline" -> {
                return new Outcome("Declined", List.of(), BigDecimal.ZERO,
                        last.message() == null || last.message().isBlank() ? last.from() + " declined" : last.message());
            }
            default -> {
                return history.size() >= maxTurns
                        ? new Outcome("Exhausted", List.of(), BigDecimal.ZERO, "No agreement within " + maxTurns + " turns")
                        : null;
            }
        }
    }

    public static BigDecimal total(List<Contracts.BotLine> lines) {
        return lines.stream().map(l -> l.unitPrice().multiply(BigDecimal.valueOf(l.quantity())))
                .reduce(BigDecimal.ZERO, BigDecimal::add);
    }
}
