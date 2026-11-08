namespace Crm.Api.Domain;

// Plain classes: each is BOTH the Foundgine semantic model and the EF Core entity (same pattern as the
// Foundgine SupplyChain sample). Tables/columns are configured fluently in CrmModel.cs; EF-only storage details
// (lengths, constraints, indexes, foreign keys) live in CrmStorage.cs.

/// <summary>Every CRM row belongs to exactly one tenant; the query layer scopes reads by this member.</summary>
public interface ITenantOwned
{
    string TenantId { get; }
}

/// <summary>Marker for plain EF entities outside the Foundgine semantic model (their columns are named in CrmStorage).</summary>
public interface IEfOnly;

public sealed class Customer : ITenantOwned
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
    public IReadOnlyList<Quote> Quotes { get; init; } = [];
    public IReadOnlyList<ReturnRequest> Returns { get; init; } = [];
    /// <summary>At most one row (the CRM-to-ERP link); a collection so the relationship stays a plain one-to-many.</summary>
    public IReadOnlyList<CustomerIntegrationMapping> IntegrationMappings { get; init; } = [];
}

public sealed class Contact : ITenantOwned
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

public sealed class Opportunity : ITenantOwned
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

public sealed class Quote : ITenantOwned
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
    public string OrderFailureReason { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTime UpdatedAt { get; init; }
    public string UpdatedBy { get; init; } = "";

    public IReadOnlyList<QuoteLine> Lines { get; init; } = [];
    public IReadOnlyList<ReturnRequest> Returns { get; init; } = [];
}

public sealed class QuoteLine : ITenantOwned
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

public static class MappingStatuses
{
    public const string Pending = "Pending", Active = "Active", Failed = "Failed";
}

/// <summary>CRM owns the CRM customer id; the ERP owns the ERP customer id. This row is the only link.</summary>
public sealed class CustomerIntegrationMapping : ITenantOwned
{
    public int CrmCustomerId { get; init; }
    public string TenantId { get; init; } = "";
    public string? ErpCustomerId { get; init; }
    public string Status { get; init; } = MappingStatuses.Pending;
    public int Attempt { get; init; } = 1;
    public string? LastError { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public static class ReturnStatuses
{
    public const string Requested = "Requested", Authorized = "Authorized", Rejected = "Rejected", Completed = "Completed";

    /// <summary>Requested -> Authorized | Rejected; Authorized -> Completed. Anything else is not allowed.</summary>
    public static bool CanMove(string from, string to) =>
        (from, to) is (Requested, Authorized) or (Requested, Rejected) or (Authorized, Completed);
}

public sealed class ReturnRequest : ITenantOwned
{
    public int Id { get; init; }
    public string TenantId { get; init; } = "";
    public int QuoteId { get; init; }
    public int CustomerId { get; init; }
    public string Status { get; init; } = ReturnStatuses.Requested;
    public string Reason { get; init; } = "";
    public string ErpReturnId { get; init; } = "";
    public string DecisionNote { get; init; } = "";
    public string RequestedBy { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public IReadOnlyList<ReturnRequestLine> Lines { get; init; } = [];
}

public sealed class ReturnRequestLine : ITenantOwned
{
    public int Id { get; init; }
    public string TenantId { get; init; } = "";
    public int ReturnId { get; init; }
    public string Sku { get; init; } = "";
    public int Quantity { get; init; }
}

// ---- EF-only entities -----------------------------------------------------------------------------------------
// Composite-key ERP mirrors and infrastructure tables. Foundgine entities have a single key column, so these are
// plain EF entities (configured in CrmStorage) and are read with LINQ through the same FoundgineDbContext.

/// <summary>Read-only mirror of an ERP shipment, keyed (tenant, ERP id); only ever moves forward by Version.</summary>
public sealed class Shipment : ITenantOwned, IEfOnly
{
    public string TenantId { get; init; } = "";
    public string ErpShipmentId { get; init; } = "";
    public int QuoteId { get; init; }
    public string ErpOrderId { get; init; } = "";
    public string Status { get; init; } = "";
    public string Carrier { get; init; } = "";
    public string TrackingNumber { get; init; } = "";
    public int Version { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed class Invoice : ITenantOwned, IEfOnly
{
    public string TenantId { get; init; } = "";
    public string ErpInvoiceId { get; init; } = "";
    public int QuoteId { get; init; }
    public int CustomerId { get; init; }
    public string ErpOrderId { get; init; } = "";
    public string Number { get; init; } = "";
    public decimal Total { get; init; }
    public string Status { get; init; } = "";
    public DateTime IssuedAt { get; init; }
    public DateTime? DueAt { get; init; }
    public int Version { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed class Payment : ITenantOwned, IEfOnly
{
    public string TenantId { get; init; } = "";
    public string ErpPaymentId { get; init; } = "";
    public string ErpInvoiceId { get; init; } = "";
    public int QuoteId { get; init; }
    public decimal Amount { get; init; }
    public string Status { get; init; } = "";
    public int Version { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>Transactional outbox row, written in the same transaction as the business change.</summary>
public sealed class OutboxMessage : ITenantOwned, IEfOnly
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = "";
    public string Type { get; init; } = "";
    public int AggregateId { get; init; }
    public int Attempt { get; init; } = 1;
    public string Payload { get; init; } = "{}";
    public DateTime CreatedAt { get; init; }
    public DateTime? PublishedAt { get; init; }
    public int PublishAttempts { get; init; }
    public DateTime NextAttemptAt { get; init; }
    public string? LastError { get; init; }
    /// <summary>Keycloak subject that caused the message (audit/propagation to Temporal); null for system-originated rows.</summary>
    public string? Initiator { get; init; }
}

public sealed class AdminAuditEntry : ITenantOwned, IEfOnly
{
    public long Id { get; init; }
    public string TenantId { get; init; } = "";
    public string UserId { get; init; } = "";
    public string Action { get; init; } = "";
    public string Target { get; init; } = "";
    public string Outcome { get; init; } = "";
    public string Detail { get; init; } = "";
    public string CorrelationId { get; init; } = "";
    public DateTime OccurredAt { get; init; }
}
