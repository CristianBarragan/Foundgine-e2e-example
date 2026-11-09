using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Foundgine.Extensions.GraphQL.HotChocolate;

namespace Foundgine.Crm.Api.GraphQL;

/// <summary>
/// Projects the scalar root selection returned by Foundgine into GraphQL's
/// { data: { entity: [...] } } response envelope. The current Foundgine query
/// executor returns provider-neutral execution cells but not the semantic
/// result tree/plan needed to materialize nested relationships. Those are
/// rejected rather than silently returned with an incorrect shape.
/// </summary>
public static class GraphQLResponseShaper
{
    public static object Shape(
        SemanticModel model,
        EntityId rootEntityId,
        GraphQLQueryExecutionResult execution)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(execution);

        var entity = model.Get(rootEntityId);
        if (execution.ResultShape.Fields.Any(field => field.Relationship is not null))
            throw new NotSupportedException(
                "Nested GraphQL relationship selections are not yet supported by the CRM response materializer.");

        var rows = execution.Execution.Rows.Select(row =>
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var field in execution.ResultShape.Fields)
            {
                if (field.Field is not { } fieldId)
                    continue;

                var cell = row.EffectiveCells.FirstOrDefault(pair =>
                    pair.Key.EntityId == rootEntityId && pair.Key.FieldId == fieldId);
                if (row.EffectiveCells.Keys.Any(key => key.EntityId == rootEntityId && key.FieldId == fieldId))
                {
                    result[field.Alias] = cell.Value;
                    continue;
                }

                var semanticField = entity.Fields.FirstOrDefault(candidate => candidate.Id == fieldId);
                var providerValue = semanticField is null
                    ? default(KeyValuePair<string, object?>)
                    : row.Values.FirstOrDefault(pair =>
                    {
                        // SQL result names are __fg_{nodeId}_{fieldName}; do not
                        // assume a particular node ID when projecting provider rows.
                        if (!pair.Key.StartsWith("__fg_", StringComparison.Ordinal)) return false;
                        var separator = pair.Key.IndexOf('_', 5);
                        return separator > 5 &&
                               int.TryParse(pair.Key.AsSpan(5, separator - 5), out _) &&
                               string.Equals(pair.Key[(separator + 1)..], semanticField.Name,
                                   StringComparison.Ordinal);
                    });
                result[field.Alias] = providerValue.Key is not null ? providerValue.Value : null;
            }
            return result;
        }).ToArray();

        var rootName = char.ToLowerInvariant(entity.Name[0]) + entity.Name[1..];
        return new Dictionary<string, object?>
        {
            ["data"] = new Dictionary<string, object?>
            {
                [rootName] = rows
            }
        };
    }
}
