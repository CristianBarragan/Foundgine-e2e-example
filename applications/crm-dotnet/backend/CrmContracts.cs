using System.Security.Claims;

namespace Foundgine.Crm.Api;

// JSON shapes returned to the frontend. These are plain DTOs: they are not EF entities and
// not Foundgine models, so the HTTP contract can change independently of storage and semantics.
public sealed class Company
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Industry { get; set; }
    public string? Website { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Contact
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public Guid? CompanyId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public sealed class Opportunity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public string Stage { get; set; } = "Qualification";
    public DateTimeOffset? ExpectedCloseDate { get; set; }
}

public sealed record CreateCompany(string Name, string? Industry, string? Website);
public sealed record CreateContact(string FirstName, string LastName, string? Email, string? Phone, Guid? CompanyId);
public sealed record CreateOpportunity(string Name, Guid CompanyId, decimal Amount, string? Stage, DateTimeOffset? ExpectedCloseDate);

/// <summary>A request the caller got wrong (maps to HTTP 400). Messages are safe to return.</summary>
public sealed class CrmValidationException(string message) : Exception(message);

/// <summary>
/// The caller's identity as established by the host from the validated Keycloak token.
/// Nothing in here can be supplied by a request body, query string or an AI tool argument.
/// </summary>
public sealed record CrmScope(string TenantId, bool CanWrite, bool CanReadSensitive = false)
{
    public static readonly string[] WriteRoles = ["crm-admin", "sales-manager", "sales-representative"];
    public const string IntegrationRole = "orchestration-service";
    public static readonly string[] SensitiveReadRoles = ["crm-admin", "sales-manager", "sales-representative"];
    /// <summary>Roles that receive Foundgine read warrants (the Keycloak RoleGrants keys).</summary>
    public static readonly string[] ReadRoles = ["crm-admin", "sales-manager", "sales-representative", "crm-readonly"];

    public static CrmScope From(ClaimsPrincipal user)
    {
        var tenant = user.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrWhiteSpace(tenant))
            throw new UnauthorizedAccessException("Token has no tenant_id claim.");
        return new CrmScope(tenant, WriteRoles.Any(user.IsInRole), SensitiveReadRoles.Any(user.IsInRole));
    }
}

public interface ICrmStore
{
    Task<IReadOnlyList<Company>> GetCompaniesAsync(CrmScope scope, CancellationToken ct);
    Task<Company> CreateCompanyAsync(CrmScope scope, CreateCompany input, CancellationToken ct);
    Task<IReadOnlyList<Contact>> GetContactsAsync(CrmScope scope, CancellationToken ct);
    Task<Contact> CreateContactAsync(CrmScope scope, CreateContact input, CancellationToken ct);
    Task<IReadOnlyList<Opportunity>> GetOpportunitiesAsync(CrmScope scope, CancellationToken ct);
    Task<Opportunity> CreateOpportunityAsync(CrmScope scope, CreateOpportunity input, CancellationToken ct);
}
