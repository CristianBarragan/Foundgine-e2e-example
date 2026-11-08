using System.Linq.Expressions;
using Crm.Api.Domain;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.IR;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.EntityFrameworkCore;

namespace Crm.Api.Application.Querying;

/// <summary>
/// Tenant-scoped semantic reads with lambda filters: <c>crm.From&lt;Customer&gt;().Eq(c =&gt; c.Status, "Active")...</c>.
/// Every query starts with the caller's tenant condition, so a scenario cannot forget it; the read still goes
/// through <see cref="FoundgineEfReader"/> (authorization, planning, EF execution).
/// </summary>
public sealed class CrmReader(FoundgineEfReader reader, ITenantContext tenant)
{
    public CrmQuery<T> From<T>() where T : class, ITenantOwned => new(reader, tenant.TenantId);
}

public sealed class CrmQuery<T> where T : class, ITenantOwned
{
    private readonly FoundgineEfReader _reader;
    private readonly SemanticQuery _query;

    internal CrmQuery(FoundgineEfReader reader, string tenantId)
    {
        _reader = reader;
        _query = reader.From(EntityId.Create(typeof(T).Name))
            .WhereEq(Filter<T>.Field(x => x.TenantId), tenantId);
    }

    public CrmQuery<T> Eq<TValue>(Expression<Func<T, TValue>> field, TValue value) => Where(Filter<T>.Eq(field, value));

    public CrmQuery<T> Where(params SemanticFilterExpression[] conditions)
    {
        _query.Where(conditions);
        return this;
    }

    public CrmQuery<T> OrderBy<TValue>(Expression<Func<T, TValue>> field)
    {
        _query.OrderBy(new SemanticOrderTerm(Filter<T>.Field(field), SemanticSortDirection.Asc));
        return this;
    }

    public CrmQuery<T> OrderByDescending<TValue>(Expression<Func<T, TValue>> field)
    {
        _query.OrderBy(new SemanticOrderTerm(Filter<T>.Field(field), SemanticSortDirection.Desc));
        return this;
    }

    public CrmQuery<T> Take(int limit)
    {
        _query.Take(limit);
        return this;
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ToRowsAsync(CancellationToken ct)
    {
        var result = await _reader.ReadAsync(_query, ct);
        return result.Rows.Select(r => (IReadOnlyDictionary<string, object?>)r.Values).ToArray();
    }
}
