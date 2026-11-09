using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Storage.Sql;
using Foundgine.Providers.Storage.Sql.Mutation;
using Npgsql;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Crm.Api.Foundgine;

public sealed record CrmRow(IReadOnlyDictionary<FieldId, object?> Values)
{
    public T Get<T>(FieldId field) =>
        Values.TryGetValue(field, out var value) && value is not null ? Convert<T>(value) : default!;

    private static T Convert<T>(object value)
    {
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (target == typeof(DateTimeOffset))
            return (T)(object)(value switch
            {
                DateTimeOffset o => o,
                DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)),
                _ => throw new InvalidCastException($"Cannot read {value.GetType().Name} as DateTimeOffset.")
            });
        return (T)System.Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// The only place the CRM touches the database. Every read and write is expressed as semantic intent
/// and goes: intent -> authorization (per-request tenant policy) -> plan -> SQL provider.
/// </summary>
public sealed class CrmSemanticExecutor
{
    private const int RootNode = 1;

    private readonly NpgsqlDataSource _dataSource;
    private readonly MetadataRegistry _metadata;
    private readonly SemanticContractSnapshot _contract;
    private readonly Planner _planner;

    public CrmSemanticExecutor(
        NpgsqlDataSource dataSource, MetadataRegistry metadata,
        SemanticContractSnapshot contract, Planner planner)
    {
        _dataSource = dataSource;
        _metadata = metadata;
        _contract = contract;
        _planner = planner;
    }

    public async Task<IReadOnlyList<CrmRow>> ReadAsync(
        CrmScope scope, EntityId entity, IReadOnlyList<FieldId> fields,
        SemanticFilterExpression? filter = null, IReadOnlyList<SemanticOrderTerm>? order = null,
        CancellationToken ct = default)
    {
        var policy = CrmAuthorization.Create(scope);
        var operation = new SemanticOperation(new SemanticReadNode(
            RootNode, entity, fields, null, null, [],
            new SemanticQueryOptions(filter, order ?? [])));

        // Authorization result is the security boundary; planning consumes it (never the bare operation),
        // so the executable plan carries authorization provenance.
        var authorization = new SemanticAuthorizer(policy).AuthorizeWithEvidence(_contract, operation);
        authorization.EnsureMatches(_contract);
        var plan = _planner.Plan(_contract, authorization);
        var sqlPlan = new SqlCompiler(_metadata).Compile(plan);

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        var result = await new SqlExecutionProvider(connection)
            .ExecuteAsync(sqlPlan, TenantContext(scope), ct);

        return result.Rows
            .Select(row => new CrmRow(row.EffectiveCells
                .Where(cell => cell.Key.NodeId == RootNode)
                .ToDictionary(cell => cell.Key.FieldId, cell => cell.Value)))
            .ToList();
    }

    /// <summary>
    /// Inserts one row. The tenant is stamped here from <paramref name="scope"/>; a caller-supplied
    /// TenantId is rejected. Writes are not constrained by the read predicate (MutationAuthorizer in
    /// Foundgine.Core does not evaluate predicates), so this method is where tenant ownership of new
    /// rows is enforced.
    /// </summary>
    public CrmRow Create(
        CrmScope scope, EntityId entity, IReadOnlyList<SemanticMutationField> values,
        IReadOnlyList<FieldId> returnFields, FieldId tenantField, CancellationToken ct = default)
    {
        if (values.Any(v => v.Field == tenantField))
            throw new InvalidOperationException("TenantId is assigned by the host and cannot be supplied.");

        var fields = values.Append(new SemanticMutationField(tenantField, scope.TenantId)).ToList();
        var operation = SemanticMutationBuilder.Create(entity, fields, returnFields);
        var graph = new SemanticMutationOperationGraph([operation]);

        // Keep one canonical semantic mutation plan through authorization and lowering.
        // The provider never receives an independently reconstructed, unauthorised plan.
        var semanticPlan = new SemanticMutationPlanner().Plan(graph);
        var authorizedPlan = new MutationAuthorizer(_metadata, CrmAuthorization.Create(scope))
            .Authorize(semanticPlan);
        var executionIr = new SemanticMutationExecutionLowerer(_metadata).Lower(authorizedPlan);

        using var connection = _dataSource.OpenConnection();
        var result = new SqlMutationExecutionProvider(connection, metadata: _metadata)
            .ExecuteBatch(executionIr, new ExecutionContext(), ct);

        var returned = result.Results.Single().ReturnedValues
                       ?? throw new InvalidOperationException("Insert returned no row.");
        return new CrmRow(returned);
    }

    private static ExecutionContext TenantContext(CrmScope scope) =>
        new(new Dictionary<string, object?> { [CrmAuthorization.TenantContextKey] = scope.TenantId });
}
