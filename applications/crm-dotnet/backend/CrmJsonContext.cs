using System.Text.Json.Serialization;
using Foundgine.Crm.Api.Foundgine;
using Foundgine.Crm.Api.GraphQL;

namespace Foundgine.Crm.Api;

/// <summary>Compile-time JSON metadata used by Minimal APIs and Native AOT publishing.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Company))]
[JsonSerializable(typeof(Contact))]
[JsonSerializable(typeof(Opportunity))]
[JsonSerializable(typeof(CreateCompany))]
[JsonSerializable(typeof(CreateContact))]
[JsonSerializable(typeof(CreateOpportunity))]
[JsonSerializable(typeof(IReadOnlyList<Company>))]
[JsonSerializable(typeof(IReadOnlyList<Contact>))]
[JsonSerializable(typeof(IReadOnlyList<Opportunity>))]
[JsonSerializable(typeof(CommercialOrder))]
[JsonSerializable(typeof(CreateCommercialOrder))]
[JsonSerializable(typeof(CommercialOrderLineInput))]
[JsonSerializable(typeof(FulfillmentCallback))]
[JsonSerializable(typeof(CommercialOrderAcceptedPayload))]
[JsonSerializable(typeof(IReadOnlyList<CommercialOrder>))]
[JsonSerializable(typeof(IReadOnlyList<CommercialOrderLineInput>))]
[JsonSerializable(typeof(IReadOnlyList<CommercialOrderAcceptedLine>))]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(SemanticExecuteRequest))]
[JsonSerializable(typeof(AssistantChatRequest))]
[JsonSerializable(typeof(AssistantChatMessage))]
[JsonSerializable(typeof(AssistantChatResponse))]
[JsonSerializable(typeof(IReadOnlyList<AssistantChatMessage>))]
[JsonSerializable(typeof(GraphQLHttpRequest))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, object?>[]))]
[JsonSerializable(typeof(SemanticCapabilities))]
[JsonSerializable(typeof(SemanticEntityCapability))]
[JsonSerializable(typeof(SemanticFieldCapability))]
[JsonSerializable(typeof(SemanticCapabilityContractResponse))]
[JsonSerializable(typeof(SemanticCapabilityResponse))]
[JsonSerializable(typeof(SemanticCapabilityInputResponse))]
[JsonSerializable(typeof(SemanticCapabilityConstraintResponse))]
[JsonSerializable(typeof(SemanticCapabilityEffectResponse))]
[JsonSerializable(typeof(IReadOnlyList<SemanticCapabilityResponse>))]
[JsonSerializable(typeof(IReadOnlyList<SemanticCapabilityInputResponse>))]
[JsonSerializable(typeof(IReadOnlyList<SemanticCapabilityConstraintResponse>))]
[JsonSerializable(typeof(IReadOnlyList<SemanticCapabilityEffectResponse>))]
[JsonSerializable(typeof(IReadOnlyList<SemanticEntityCapability>))]
[JsonSerializable(typeof(IReadOnlyList<SemanticFieldCapability>))]
internal partial class CrmJsonContext : JsonSerializerContext { }

public sealed record ApiError(string Error);
public sealed record HealthResponse(string Status);
