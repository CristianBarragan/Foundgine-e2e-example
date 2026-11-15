using System.Text;
using System.Text.Json;
using Foundgine.Providers.AgentFramework.Hosting;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Temporalio.Activities;
using Temporalio.Exceptions;

namespace Crm.Api.Integration;

public sealed record SalesVerdict(string Intent, IReadOnlyList<BotLine> Lines, string Message);

/// <summary>
/// The hard rules of the customer's side of the negotiation, as pure functions (the C# counterpart of the ERP's
/// FulfilmentGuardrail). They are the whole decision in rules mode and the safety net in MAF mode: the model may propose,
/// but nothing it says can offer above the customer's price limit, below the minimum quantity, or outside the brief.
/// </summary>
public static class SalesGuardrail
{
    private const decimal OpeningFraction = 0.85m;

    /// <summary>Turn 1: the wanted quantities at an opening price below the customer's limit.</summary>
    public static SalesVerdict Open(Brief brief) => new("Propose",
        brief.Lines.Select(l => new BotLine(l.Sku, l.Quantity, Math.Round(l.MaxUnitPrice * OpeningFraction, 2))).ToList(),
        "Opening offer.");

    /// <summary>The best answer to the fulfilment bot's last message: Accept as-is, Counter within the limits, or Decline.</summary>
    public static SalesVerdict Respond(Brief brief, IReadOnlyList<BotMessage> history)
    {
        if (history.Count == 0) return Open(brief);
        var last = history[^1];
        if (Acceptable(last.Lines, brief)) return new("Accept", last.Lines, "Within the customer's limits.");

        var counter = new List<BotLine>();
        var notes = new List<string>();
        foreach (var want in brief.Lines)
        {
            var theirs = last.Lines.FirstOrDefault(l => l.Sku == want.Sku);
            if (theirs is null) return Decline($"{want.Sku} is not offered");
            if (theirs.Quantity < want.MinQuantity)
                return Decline($"{want.Sku}: only {theirs.Quantity} offered, the customer needs at least {want.MinQuantity}");
            var qty = Math.Min(theirs.Quantity, want.Quantity);
            var price = Math.Min(theirs.UnitPrice, want.MaxUnitPrice);
            if (theirs.UnitPrice > want.MaxUnitPrice) notes.Add($"{want.Sku}: price limit is {want.MaxUnitPrice}");
            counter.Add(new BotLine(want.Sku, qty, price));
        }

        // Stalemate: the same counter was already sent and not accepted, so another round changes nothing.
        var mine = history.LastOrDefault(m => m.From == SalesBotNames.Sales);
        if (mine is not null && SameLines(mine.Lines, counter)) return Decline("No overlap between the customer's limits and the offer");
        return new("Counter", counter, notes.Count == 0 ? "Counter-offer." : "Counter-offer. " + string.Join("; ", notes));
    }

    /// <summary>
    /// Applies the guardrail to a model's proposed reply. A Decline is always allowed. Propose/Counter must stay inside the
    /// brief; Accept must repeat the fulfilment bot's lines and those must be acceptable. Anything else is replaced by the
    /// rules answer.
    /// </summary>
    public static SalesVerdict Constrain(SalesVerdict? proposed, Brief brief, IReadOnlyList<BotMessage> history)
    {
        var fallback = Respond(brief, history);
        if (proposed is null) return fallback;
        var message = proposed.Message.Length > 1000 ? proposed.Message[..1000] : proposed.Message;
        switch (proposed.Intent)
        {
            case "Decline": return new("Decline", [], message);
            case "Propose" when history.Count == 0 && WithinBrief(proposed.Lines, brief):
                return proposed with { Message = message };
            case "Counter" when history.Count > 0 && WithinBrief(proposed.Lines, brief):
                return proposed with { Message = message };
            case "Accept" when history.Count > 0 && Acceptable(history[^1].Lines, brief) && SameLines(proposed.Lines, history[^1].Lines):
                return new("Accept", history[^1].Lines, message);
            default: return fallback;
        }
    }

    /// <summary>Every brief SKU exactly once, nothing else; quantity within [minQuantity, quantity]; price at or below the limit.</summary>
    public static bool Acceptable(IReadOnlyList<BotLine> lines, Brief brief) => WithinBrief(lines, brief)
        && brief.Lines.All(want => lines.Count(l => l.Sku == want.Sku) == 1);

    /// <summary>Only brief SKUs, no duplicates, each inside the customer's quantity and price limits.</summary>
    public static bool WithinBrief(IReadOnlyList<BotLine> lines, Brief brief)
    {
        if (lines.Count == 0 || lines.Select(l => l.Sku).Distinct().Count() != lines.Count) return false;
        foreach (var l in lines)
        {
            var want = brief.Lines.FirstOrDefault(b => b.Sku == l.Sku);
            if (want is null || l.Quantity < want.MinQuantity || l.Quantity > want.Quantity
                || l.UnitPrice < 0 || l.UnitPrice > want.MaxUnitPrice) return false;
        }
        return true;
    }

    public static bool SameLines(IReadOnlyList<BotLine> a, IReadOnlyList<BotLine> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Sku == p.Second.Sku && p.First.Quantity == p.Second.Quantity
                                                 && p.First.UnitPrice == p.Second.UnitPrice);

    private static SalesVerdict Decline(string reason) => new("Decline", [], reason);
}

/// <summary>Decides one sales turn. Implementations never write anywhere; the result is advisory.</summary>
public interface ISalesBrain
{
    Task<SalesVerdict> DecideAsync(Brief brief, IReadOnlyList<BotMessage> history, CancellationToken ct);
}

/// <summary>Deterministic sales bot: the guardrail is the answer. Default, so demos and CI need no model or API key.</summary>
public sealed class RuleSalesBrain : ISalesBrain
{
    public Task<SalesVerdict> DecideAsync(Brief brief, IReadOnlyList<BotMessage> history, CancellationToken ct) =>
        Task.FromResult(SalesGuardrail.Respond(brief, history));
}

/// <summary>
/// Sales bot driven by a Microsoft Agent Framework agent built by <c>AddFoundgineAgentFramework</c>. The agent gets the
/// negotiation as text and answers with one JSON message; <see cref="SalesGuardrail.Constrain"/> then enforces the customer's
/// limits, and any failure (model error, bad JSON) falls back to the rules. The activity has no bearer token, so the
/// Foundgine data tools of the agent fail closed here by design; the bot decides from the brief and the transcript only.
/// </summary>
public sealed class MafSalesBrain(AIAgent agent, ILogger<MafSalesBrain> log) : ISalesBrain
{
    public const string AgentName = "crm-sales-bot";

    public const string Instructions = """
        You are the sales bot negotiating for a customer against a fulfilment bot. Reply with ONE JSON object and nothing else:
        {"intent":"Propose|Counter|Accept|Decline","lines":[{"sku":"...","quantity":1,"unitPrice":0.00}],"message":"short text"}
        Rules: never go above a line's maxUnitPrice, never below its minQuantity or above its quantity, never add SKUs that are
        not in the brief. Accept repeats the other side's lines exactly. Decline carries no lines. Do not call tools.
        """;

    public async Task<SalesVerdict> DecideAsync(Brief brief, IReadOnlyList<BotMessage> history, CancellationToken ct)
    {
        SalesVerdict? proposed = null;
        try
        {
            var session = await agent.CreateSessionAsync();
            var response = await agent.RunAsync(Prompt(brief, history), session).WaitAsync(ct);
            proposed = Parse(response.Text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Sales bot model call failed; using the rules answer");
        }
        return SalesGuardrail.Constrain(proposed, brief, history);
    }

    public static string Prompt(Brief brief, IReadOnlyList<BotMessage> history)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Customer brief: " + JsonSerializer.Serialize(brief));
        sb.AppendLine(history.Count == 0
            ? "No messages yet: make the opening Propose."
            : "Transcript so far: " + JsonSerializer.Serialize(history));
        sb.AppendLine("Answer with the JSON object only.");
        return sb.ToString();
    }

    public static SalesVerdict? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            var root = doc.RootElement;
            var intent = root.GetProperty("intent").GetString() ?? "";
            var lines = new List<BotLine>();
            if (root.TryGetProperty("lines", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var l in arr.EnumerateArray())
                    lines.Add(new BotLine(l.GetProperty("sku").GetString() ?? "", l.GetProperty("quantity").GetInt32(),
                        l.GetProperty("unitPrice").GetDecimal()));
            var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            return new SalesVerdict(intent, lines, message);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// The CRM side of BotNegotiationWorkflow (hosted by the Java orchestration worker). Called by the Java workflow on the "crm"
/// queue by activity name. Inputs carry the tenant explicitly; no token enters workflow history and nothing is written.
/// </summary>
public sealed class SalesBotActivities(ISalesBrain brain)
{
    [Activity(SalesBotNames.TakeTurnActivity)]
    public async Task<BotMessage> TakeTurn(BotTurnInput input) =>
        await TakeTurnAsync(input, ActivityExecutionContext.Current.CancellationToken);

    public async Task<BotMessage> TakeTurnAsync(BotTurnInput input, CancellationToken ct)
    {
        Validate(input);
        var verdict = await brain.DecideAsync(input.Brief!, input.History, ct);
        // The workflow re-checks every message (NegotiationRules); a malformed one fails the workflow, not the CRM.
        return new BotMessage(1, SalesBotNames.Sales, input.Turn, verdict.Intent, verdict.Lines, verdict.Message);
    }

    private static void Validate(BotTurnInput i)
    {
        string? problem = null;
        if (i.SchemaVersion != 1) problem = $"Unsupported schemaVersion {i.SchemaVersion}";
        else if (string.IsNullOrWhiteSpace(i.TenantId)) problem = "tenantId is required";
        else if (i.Turn < 1 || i.Turn % 2 == 0) problem = $"Turn {i.Turn} does not belong to the sales bot";
        else if (i.History.Count != i.Turn - 1) problem = $"Turn {i.Turn} needs {i.Turn - 1} earlier messages, got {i.History.Count}";
        else if (i.Brief is null || i.Brief.Lines.Count == 0) problem = "The sales bot needs the customer's brief";
        if (problem is not null) throw new ApplicationFailureException(problem, errorType: "ValidationFailed", nonRetryable: true);
    }
}

public static class SalesBotExtensions
{
    /// <summary>
    /// Registers the sales bot. <c>Bots:Mode</c> = <c>rules</c> (default, deterministic) or <c>maf</c> (Foundgine MAF adapter; the host
    /// must register an <see cref="IChatClient"/>, for example an OpenAI or Azure OpenAI client).
    /// </summary>
    public static IServiceCollection AddCrmSalesBot(this IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration["Bots:Mode"] ?? "rules";
        services.AddScoped<RuleSalesBrain>();
        if (!string.Equals(mode, "maf", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<ISalesBrain>(sp => sp.GetRequiredService<RuleSalesBrain>());
            return services;
        }

        if (!services.Any(d => d.ServiceType == typeof(IChatClient)))
            throw new InvalidOperationException(
                "Bots:Mode=maf needs an IChatClient registered in DI (Microsoft.Extensions.AI), e.g. an OpenAI chat client.");
        services.AddFoundgineAgentFramework(o =>
        {
            o.AgentName = MafSalesBrain.AgentName;
            o.AgentDescription = "Negotiates for the customer against the ERP fulfilment bot.";
            o.Instructions = MafSalesBrain.Instructions;
            o.EnableMutations = false;
        });
        services.AddScoped<ISalesBrain>(sp => new MafSalesBrain(
            sp.GetRequiredKeyedService<AIAgent>(MafSalesBrain.AgentName), sp.GetRequiredService<ILogger<MafSalesBrain>>()));
        return services;
    }
}
