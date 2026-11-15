using System.Text.Json;
using Crm.Api.Integration;
using Temporalio.Exceptions;
using Xunit;

namespace Crm.Api.Tests;

public sealed class SalesBotTests
{
    private static readonly Brief Brief = new("note", [new BriefLine("SKU-1", 10, 6, 100m)]);

    private static BotMessage Sales(int turn, string intent, params BotLine[] lines) => new(1, "sales", turn, intent, lines, "");
    private static BotMessage Erp(int turn, string intent, params BotLine[] lines) => new(1, "fulfilment", turn, intent, lines, "");

    [Fact]
    public void Opening_ProposesTheWantedQuantityBelowThePriceLimit()
    {
        var v = SalesGuardrail.Open(Brief);
        Assert.Equal("Propose", v.Intent);
        var line = Assert.Single(v.Lines);
        Assert.Equal(("SKU-1", 10, 85.00m), (line.Sku, line.Quantity, line.UnitPrice));
    }

    [Fact]
    public void AcceptsAnOfferInsideTheLimits_RepeatingTheLinesExactly()
    {
        var offer = new BotLine("SKU-1", 8, 95m);
        var v = SalesGuardrail.Respond(Brief, [Sales(1, "Propose", new BotLine("SKU-1", 10, 85m)), Erp(2, "Counter", offer)]);
        Assert.Equal("Accept", v.Intent);
        Assert.True(SalesGuardrail.SameLines(v.Lines, [offer]));
    }

    [Fact]
    public void CountersAtThePriceLimit_WhenTheOfferIsTooExpensive()
    {
        var v = SalesGuardrail.Respond(Brief, [Sales(1, "Propose", new BotLine("SKU-1", 10, 85m)), Erp(2, "Counter", new BotLine("SKU-1", 10, 120m))]);
        Assert.Equal("Counter", v.Intent);
        Assert.Equal(100m, v.Lines[0].UnitPrice);
    }

    [Fact]
    public void Declines_WhenTooLittleStockIsOffered_OrTheCounterRepeats()
    {
        var tooFew = SalesGuardrail.Respond(Brief, [Sales(1, "Propose", new BotLine("SKU-1", 10, 85m)), Erp(2, "Counter", new BotLine("SKU-1", 3, 90m))]);
        Assert.Equal("Decline", tooFew.Intent);
        Assert.Empty(tooFew.Lines);

        var stalemate = SalesGuardrail.Respond(Brief, [
            Sales(1, "Propose", new BotLine("SKU-1", 10, 85m)), Erp(2, "Counter", new BotLine("SKU-1", 10, 120m)),
            Sales(3, "Counter", new BotLine("SKU-1", 10, 100m)), Erp(4, "Counter", new BotLine("SKU-1", 10, 120m))]);
        Assert.Equal("Decline", stalemate.Intent);
    }

    [Fact]
    public void Constrain_NeverLetsAModelExceedTheCustomersLimits()
    {
        var history = new[] { Sales(1, "Propose", new BotLine("SKU-1", 10, 85m)), Erp(2, "Counter", new BotLine("SKU-1", 10, 120m)) };
        // Model tries to accept an offer above the limit, to counter above the limit, and to add a SKU outside the brief.
        foreach (var bad in new[]
                 {
                     new SalesVerdict("Accept", history[1].Lines, "ok"),
                     new SalesVerdict("Counter", [new BotLine("SKU-1", 10, 150m)], "ok"),
                     new SalesVerdict("Counter", [new BotLine("SKU-9", 1, 1m)], "ok"),
                     new SalesVerdict("Bogus", [], "ok")
                 })
        {
            var v = SalesGuardrail.Constrain(bad, Brief, history);
            Assert.Equal("Counter", v.Intent);               // the rules answer, not the model's
            Assert.All(v.Lines, l => Assert.True(l.UnitPrice <= 100m));
        }
        Assert.Equal("Decline", SalesGuardrail.Constrain(new SalesVerdict("Decline", [new BotLine("SKU-1", 1, 1m)], "no"), Brief, history).Intent);
        Assert.Empty(SalesGuardrail.Constrain(new SalesVerdict("Decline", [new BotLine("SKU-1", 1, 1m)], "no"), Brief, history).Lines);
    }

    [Fact]
    public void MafReplyParsing_ToleratesProseAroundTheJson_AndRejectsGarbage()
    {
        var v = MafSalesBrain.Parse("Sure!\n{\"intent\":\"Counter\",\"lines\":[{\"sku\":\"SKU-1\",\"quantity\":8,\"unitPrice\":90.5}],\"message\":\"hi\"}");
        Assert.NotNull(v);
        Assert.Equal(("Counter", 8, 90.5m), (v.Intent, v.Lines[0].Quantity, v.Lines[0].UnitPrice));
        Assert.Null(MafSalesBrain.Parse("no json here"));
        Assert.Null(MafSalesBrain.Parse("{\"intent\":"));
    }

    [Fact]
    public async Task Activity_ProducesTheMessageTheJavaWorkflowExpects()
    {
        var activities = new SalesBotActivities(new RuleSalesBrain());
        var first = await activities.TakeTurnAsync(new BotTurnInput(1, "acme", "c", "n1", 1, 42, Brief, []), default);
        Assert.Equal(("sales", 1, "Propose"), (first.From, first.Turn, first.Intent));

        var json = JsonSerializer.Serialize(first);
        Assert.Contains("\"unitPrice\":85", json);
        Assert.Contains("\"schemaVersion\":1", json);
    }

    [Fact]
    public async Task Activity_RejectsAnotherPartysTurn_OrAMissingBrief_AsNonRetryable()
    {
        var activities = new SalesBotActivities(new RuleSalesBrain());
        var evenTurn = await Assert.ThrowsAsync<ApplicationFailureException>(() =>
            activities.TakeTurnAsync(new BotTurnInput(1, "acme", "c", "n1", 2, 42, Brief, [Sales(1, "Propose", new BotLine("SKU-1", 10, 85m))]), default));
        Assert.True(evenTurn.NonRetryable);
        var noBrief = await Assert.ThrowsAsync<ApplicationFailureException>(() =>
            activities.TakeTurnAsync(new BotTurnInput(1, "acme", "c", "n1", 1, 42, null, []), default));
        Assert.Equal("ValidationFailed", noBrief.ErrorType);
    }

    [Fact]
    public void TurnInput_DeserializesTheJsonTheJavaWorkflowSends()
    {
        const string json = """
        {"schemaVersion":1,"tenantId":"acme","correlationId":"c","negotiationId":"n1","turn":3,"crmCustomerId":42,
         "brief":{"note":"n","lines":[{"sku":"SKU-1","quantity":10,"minQuantity":6,"maxUnitPrice":100.00}]},
         "history":[{"schemaVersion":1,"from":"sales","turn":1,"intent":"Propose","lines":[{"sku":"SKU-1","quantity":10,"unitPrice":85.00}],"message":"m"},
                    {"schemaVersion":1,"from":"fulfilment","turn":2,"intent":"Counter","lines":[{"sku":"SKU-1","quantity":8,"unitPrice":95.00}],"message":"m"}]}
        """;
        var input = JsonSerializer.Deserialize<BotTurnInput>(json);
        Assert.NotNull(input);
        Assert.Equal(3, input.Turn);
        Assert.Equal(100.00m, input.Brief!.Lines[0].MaxUnitPrice);
        Assert.Equal(2, input.History.Count);
        Assert.Equal(95.00m, input.History[1].Lines[0].UnitPrice);
    }
}
