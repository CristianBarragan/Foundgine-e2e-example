using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Providers.Storage.Sql;
using Foundgine.Providers.Storage.Sql.Mutation.Postgres;
using Npgsql;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Crm.Api.Foundgine;

/// <summary>
/// Pooled-connection adapter for Foundgine's SQL query provider. The compiler and
/// actual SQL execution remain inside Foundgine.Providers; this class only scopes
/// a physical connection to one execution.
/// </summary>
public sealed class PooledCrmSqlExecutionProvider(NpgsqlDataSource dataSource) : IExecutionProvider
{
    public async Task<ExecutionResult> ExecuteAsync(
        ProviderPlan plan,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await new SqlExecutionProvider(connection).ExecuteAsync(plan, context, cancellationToken);
    }
}

/// <summary>
/// Connection-scoped adapter for Foundgine's PostgreSQL batched mutation provider.
/// The runtime still performs mutation planning, authorization and security
/// certification before this provider is invoked.
/// </summary>
public sealed class PooledCrmPostgresMutationProvider(
    NpgsqlDataSource dataSource,
    IMetadataProvider metadata) : IMutationBatchExecutionProvider, IMutationSecurityConformanceEvaluator
{
    public MutationSecurityConformanceResult Evaluate(ExecutionMutationIR ir)
    {
        // The provider's conformance proof is compiler-based; an unopened
        // Npgsql connection is sufficient and no database round trip occurs.
        using var connection = new NpgsqlConnection();
        var proof = new PostgresBatchedMutationExecutionProvider(connection, metadata).Evaluate(ir);
        return proof with { Provider = GetType().FullName ?? GetType().Name };
    }

    public MutationBatchResult ExecuteBatch(ExecutionMutationIR ir, ExecutionContext context)
    {
        using var connection = dataSource.OpenConnection();
        return new PostgresBatchedMutationExecutionProvider(connection, metadata)
            .ExecuteBatch(ir, context);
    }

    public MutationBatchResult ExecuteBatch(
        ExecutionMutationIR ir,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = dataSource.OpenConnection();
        return new PostgresBatchedMutationExecutionProvider(connection, metadata)
            .ExecuteBatch(ir, context, cancellationToken);
    }
}
