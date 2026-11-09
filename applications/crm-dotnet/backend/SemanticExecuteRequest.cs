namespace Foundgine.Crm.Api;

/// <summary>
/// Dynamic semantic execution request. Intent may be natural language or a
/// provider-neutral Foundgine JSON read intent. Identity and tenant are never inputs.
/// </summary>
public sealed record SemanticExecuteRequest(string Intent);
