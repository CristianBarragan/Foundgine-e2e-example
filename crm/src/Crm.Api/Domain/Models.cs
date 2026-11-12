namespace Crm.Api.Domain;

// Plain classes: each is BOTH the Foundgine semantic model and the EF Core entity (same pattern as the
// Foundgine SupplyChain sample). Tables/columns are configured fluently in CrmModel.cs.

public sealed class Customer
{
    public int Id { get; init; }
    public string TenantId { get; init; } = "";
    public string Kind { get; init; } = CustomerKinds.Business;
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
    public string Status { get; init; } = CustomerStatuses.Active;
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTime UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = "";

    public IReadOnlyList<Contact> Contacts { get; init; } = [];
    public IReadOnlyList<Opportunity> Opportunities { get; init; } = [];
}

public sealed class Contact
{
    public int Id { get; init; }
    public string TenantId { get; init; } = "";
    public int CustomerId { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
    public string Phone { get; init; } = "";
    public bool IsPrimary { get; init; }
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTime UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = "";
}

public static class CustomerKinds
{
    public const string Business = "Business";
    public const string Individual = "Individual";
    public static bool IsValid(string? v) => v is Business or Individual;
}

public static class CustomerStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public static bool IsValid(string? v) => v is Active or Inactive;
}

public sealed class Opportunity
{
    public int Id { get; init; }
    public string TenantId { get; init; } = "";
    public int CustomerId { get; init; }
    public string Name { get; init; } = "";
    public string Stage { get; init; } = OpportunityStages.Prospecting;
    public decimal Amount { get; init; }
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTime UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = "";

    public IReadOnlyList<Quote> Quotes { get; init; } = [];
}

public sealed class Quote
{
    public int Id { get; init; }
    public string TenantId { get; init; } = "";
    public int OpportunityId { get; init; }
    public int CustomerId { get; init; }
    public string Status { get; init; } = QuoteStatuses.Draft;
    public decimal Total { get; init; }
    public string SubmittedBy { get; init; } = "";
    public string DecidedBy { get; init; } = "";
    public string ErpOrderId { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTime UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = "";

    public IReadOnlyList<QuoteLine> Lines { get; init; } = [];
}

public sealed class QuoteLine
{
    public int Id { get; init; }
    public string TenantId { get; init; } = "";
    public int QuoteId { get; init; }
    public string Sku { get; init; } = "";
    public string Description { get; init; } = "";
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

public static class OpportunityStages
{
    public const string Prospecting = "Prospecting", Qualification = "Qualification", Proposal = "Proposal",
        Negotiation = "Negotiation", ClosedWon = "ClosedWon", ClosedLost = "ClosedLost";

    public static readonly string[] Open = [Prospecting, Qualification, Proposal, Negotiation];
    public static bool IsOpen(string s) => Array.IndexOf(Open, s) >= 0;

    /// <summary>Forward one step at a time, or to ClosedLost from any open stage. ClosedWon only via quote acceptance.</summary>
    public static bool CanMove(string from, string to)
    {
        var i = Array.IndexOf(Open, from);
        if (i < 0) return false;
        return to == ClosedLost || (i + 1 < Open.Length && Open[i + 1] == to);
    }
}

public static class QuoteStatuses
{
    public const string Draft = "Draft", PendingApproval = "PendingApproval", Approved = "Approved",
        Rejected = "Rejected", Accepted = "Accepted", Ordered = "Ordered", OrderFailed = "OrderFailed";
}
