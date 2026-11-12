namespace Crm.Api.Application;

public sealed record CustomerRequest(string? Kind, string? Name, string? Email, string? Phone, string? Status);

public sealed record ContactRequest(string? FirstName, string? LastName, string? Email, string? Phone, bool? IsPrimary);

public sealed record PagedResult(IReadOnlyList<IReadOnlyDictionary<string, object?>> Items, int Skip, int Take);

public sealed record OpportunityRequest(string? Name, decimal? Amount, string? Stage);

public sealed record QuoteLineRequest(string? Sku, string? Description, int? Quantity, decimal? UnitPrice);

public sealed record QuoteRequest(IReadOnlyList<QuoteLineRequest>? Lines);

public sealed record QuoteView(Domain.Quote Quote, IReadOnlyList<Domain.QuoteLine> Lines);

public sealed class QuoteOptions
{
    /// <summary>Quotes above this total need approval by a different admin; others are approved on submit.</summary>
    public decimal ApprovalThreshold { get; set; } = 10_000m;
}
