using System.Text.Json.Serialization;

namespace Crm.Api.Integration;

// Language-neutral contracts (see contracts/schemas). Explicit JSON names so the Java side sees camelCase.

public sealed record CustomerSyncDto(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("crmCustomerId")] int CrmCustomerId,
    [property: JsonPropertyName("attempt")] int Attempt,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("status")] string Status);

public sealed record CustomerRef(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("crmCustomerId")] int CrmCustomerId);

public sealed record RecordErpCustomerInput(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("crmCustomerId")] int CrmCustomerId,
    [property: JsonPropertyName("erpCustomerId")] string ErpCustomerId);

public sealed record MarkSyncFailedInput(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("crmCustomerId")] int CrmCustomerId,
    [property: JsonPropertyName("reason")] string Reason);

public static class IntegrationNames
{
    public const string OnboardingWorkflow = "CustomerOnboardingWorkflow";
    public const string OnboardingRequested = "CustomerOnboardingRequested";
    public const string GetCustomerActivity = "crm.get-customer-for-sync";
    public const string RecordErpCustomerActivity = "crm.record-erp-customer";
    public const string MarkSyncFailedActivity = "crm.mark-sync-failed";

    /// <summary>Deterministic: the same business key + attempt can never start two workflows.</summary>
    public static string WorkflowId(string tenantId, int crmCustomerId, int attempt) =>
        attempt <= 1
            ? $"CustomerOnboarding:{tenantId}:{crmCustomerId}"
            : $"CustomerOnboarding:{tenantId}:{crmCustomerId}:{attempt}";
}
