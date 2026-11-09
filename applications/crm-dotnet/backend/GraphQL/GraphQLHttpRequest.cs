using System.Text.Json;

namespace Foundgine.Crm.Api.GraphQL;

/// <summary>
/// GraphQL-over-HTTP request envelope. The query text is translated by
/// Foundgine.Extensions' HotChocolateSemanticAdapter; variables contain only
/// semantic query arguments and never caller identity or tenant authority.
/// </summary>
public sealed record GraphQLHttpRequest(
    string Query,
    Dictionary<string, JsonElement>? Variables = null,
    string? OperationName = null);
