package com.enterprise.orchestration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;
import io.temporal.client.WorkflowException;
import io.temporal.client.WorkflowOptions;
import io.temporal.testing.TestWorkflowEnvironment;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Function;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class BotNegotiationWorkflowTest {
    /** Stand-in for the C# sales bot worker (queue "crm"). */
    @ActivityInterface
    public interface SalesStub {
        @ActivityMethod(name = Contracts.CRM_SALES_BOT_TURN) Contracts.BotMessage turn(Contracts.BotTurnInput in);
    }

    /** Stand-in for the Java fulfilment bot (queue "erp"). */
    @ActivityInterface
    public interface FulfilmentStub {
        @ActivityMethod(name = Contracts.ERP_FULFILMENT_BOT_TURN) Contracts.BotMessage turn(Contracts.BotTurnInput in);
    }

    // Real classes, not lambdas: Temporal registers activities by reflecting on the instance's interfaces.
    static SalesStub sales(Function<Contracts.BotTurnInput, Contracts.BotMessage> f) {
        return new SalesStub() { public Contracts.BotMessage turn(Contracts.BotTurnInput in) { return f.apply(in); } };
    }

    static FulfilmentStub fulfilment(Function<Contracts.BotTurnInput, Contracts.BotMessage> f) {
        return new FulfilmentStub() { public Contracts.BotMessage turn(Contracts.BotTurnInput in) { return f.apply(in); } };
    }

    static Contracts.BotLine line(String sku, int qty, String price) {
        return new Contracts.BotLine(sku, qty, new BigDecimal(price));
    }

    static Contracts.BotMessage msg(String from, int turn, String intent, String text, Contracts.BotLine... lines) {
        return new Contracts.BotMessage(1, from, turn, intent, List.of(lines), text);
    }

    static final Contracts.Brief BRIEF = new Contracts.Brief("",
            List.of(new Contracts.BriefLine("SKU-1", 120, 80, new BigDecimal("95.00"))));
    static final Contracts.BotNegotiationStart START =
            new Contracts.BotNegotiationStart(1, "acme", "corr", "neg-1", 42, 6, BRIEF);

    final List<Contracts.BotTurnInput> salesInputs = new ArrayList<>();
    final List<Contracts.BotTurnInput> fulfilmentInputs = new ArrayList<>();
    TestWorkflowEnvironment env;

    @BeforeEach void setUp() { env = TestWorkflowEnvironment.newInstance(); }
    @AfterEach void tearDown() { env.close(); }

    private Contracts.BotNegotiationResult run(SalesStub sales, FulfilmentStub fulfilment) {
        var orchestration = env.newWorker(Contracts.ORCHESTRATION_QUEUE);
        orchestration.registerWorkflowImplementationTypes(BotNegotiationWorkflowImpl.class);
        env.newWorker(Contracts.CRM_QUEUE).registerActivitiesImplementations(sales);
        env.newWorker(Contracts.ERP_QUEUE).registerActivitiesImplementations(fulfilment);
        env.start();
        var wf = env.getWorkflowClient().newWorkflowStub(BotNegotiationWorkflow.class,
                WorkflowOptions.newBuilder().setTaskQueue(Contracts.ORCHESTRATION_QUEUE)
                        .setWorkflowId("BotNegotiation:acme:neg-1").build());
        return wf.negotiate(START);
    }

    /** Sales proposes 120 @ 85; fulfilment has 100 and a 90 floor; sales is within its limits and accepts. */
    @Test void the_bots_converge_on_a_counter_offer() {
        SalesStub sales = sales(in -> {
            salesInputs.add(in);
            return in.turn() == 1
                    ? msg("sales", 1, "Propose", "opening", line("SKU-1", 120, "85.00"))
                    : msg("sales", in.turn(), "Accept", "ok", in.history().get(in.history().size() - 1).lines().toArray(Contracts.BotLine[]::new));
        });
        FulfilmentStub fulfilment = fulfilment(in -> {
            fulfilmentInputs.add(in);
            return msg("fulfilment", in.turn(), "Counter", "only 100, floor 90", line("SKU-1", 100, "90.00"));
        });
        var result = run(sales, fulfilment);
        assertEquals("Agreed", result.status());
        assertEquals(3, result.turns());
        assertEquals(new BigDecimal("9000.00"), result.total());
        assertEquals(3, result.transcript().size());
    }

    @Test void the_customers_limits_reach_the_sales_bot_only() {
        SalesStub sales = sales(in -> {
            salesInputs.add(in);
            return msg("sales", in.turn(), in.turn() == 1 ? "Propose" : "Decline", "stop",
                    in.turn() == 1 ? new Contracts.BotLine[] {line("SKU-1", 120, "85.00")} : new Contracts.BotLine[0]);
        });
        FulfilmentStub fulfilment = fulfilment(in -> {
            fulfilmentInputs.add(in);
            return msg("fulfilment", in.turn(), "Counter", "x", line("SKU-1", 100, "90.00"));
        });
        run(sales, fulfilment);
        assertTrue(salesInputs.stream().allMatch(i -> i.brief() != null));
        assertTrue(!fulfilmentInputs.isEmpty() && fulfilmentInputs.stream().allMatch(i -> i.brief() == null));
    }

    @Test void a_decline_ends_the_negotiation_with_the_reason() {
        SalesStub sales = sales(in -> msg("sales", 1, "Propose", "open", line("SKU-9", 5, "10.00")));
        FulfilmentStub fulfilment = fulfilment(in -> msg("fulfilment", 2, "Decline", "SKU-9 is discontinued"));
        var result = run(sales, fulfilment);
        assertEquals("Declined", result.status());
        assertEquals("SKU-9 is discontinued", result.reason());
        assertTrue(result.lines().isEmpty());
    }

    @Test void endless_countering_is_cut_off_at_max_turns() {
        SalesStub sales = sales(in -> msg("sales", in.turn(), in.turn() == 1 ? "Propose" : "Counter", "again", line("SKU-1", 120, "85.00")));
        FulfilmentStub fulfilment = fulfilment(in -> msg("fulfilment", in.turn(), "Counter", "no", line("SKU-1", 100, "90.00")));
        var result = run(sales, fulfilment);
        assertEquals("Exhausted", result.status());
        assertEquals(6, result.turns());
    }

    @Test void a_bot_that_breaks_the_protocol_fails_the_workflow() {
        // Turn 1 answered by the wrong speaker.
        SalesStub sales = sales(in -> msg("fulfilment", 1, "Propose", "oops", line("SKU-1", 1, "1.00")));
        FulfilmentStub fulfilment = fulfilment(in -> msg("fulfilment", in.turn(), "Decline", "n/a"));
        assertThrows(WorkflowException.class, () -> run(sales, fulfilment));
    }
}
