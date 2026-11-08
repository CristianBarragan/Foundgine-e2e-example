using System.Linq.Expressions;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Query;

namespace Crm.Api.Application.Querying;

/// <summary>
/// Lambda-typed builders for Foundgine semantic filters. <c>Filter&lt;Customer&gt;.Eq(c =&gt; c.Status, "Active")</c>
/// resolves the semantic field id from the property (semantic entity and field names are the CLR names, as in
/// <c>CrmSemantics</c>); <c>Some / None / AtLeast</c> take a collection navigation and a predicate over its target.
/// Nothing here executes anything: the result is the core IR the planner and authorizer already handle.
/// </summary>
public static class Filter<T> where T : class
{
    private static readonly string Entity = typeof(T).Name;

    public static SemanticFilterExpression Eq<TValue>(Expression<Func<T, TValue>> field, TValue value) =>
        new SemanticFieldFilter(Field(field), SemanticFilterOperator.Eq, value);

    public static SemanticFilterExpression Neq<TValue>(Expression<Func<T, TValue>> field, TValue value) =>
        new SemanticFieldFilter(Field(field), SemanticFilterOperator.Neq, value);

    public static SemanticFilterExpression In<TValue>(Expression<Func<T, TValue>> field, params TValue[] values) =>
        new SemanticFieldFilter(Field(field), SemanticFilterOperator.In, values.Cast<object?>().ToArray());

    public static SemanticFilterExpression And(params SemanticFilterExpression[] parts) => new SemanticAndFilter(parts);

    public static SemanticFilterExpression Or(params SemanticFilterExpression[] parts) => new SemanticOrFilter(parts);

    /// <summary>At least one related row satisfies <paramref name="predicate"/>.</summary>
    public static SemanticFilterExpression Some<TChild>(Expression<Func<T, IReadOnlyList<TChild>>> relationship,
        SemanticFilterExpression predicate) =>
        new SemanticRelationshipFilter(Relationship(relationship), SemanticRelationshipQuantifier.Some, predicate);

    /// <summary>No related row satisfies <paramref name="predicate"/> (also true when there are no related rows).</summary>
    public static SemanticFilterExpression None<TChild>(Expression<Func<T, IReadOnlyList<TChild>>> relationship,
        SemanticFilterExpression predicate) =>
        new SemanticRelationshipFilter(Relationship(relationship), SemanticRelationshipQuantifier.None, predicate);

    /// <summary>At least <paramref name="count"/> related rows satisfy <paramref name="predicate"/>.</summary>
    public static SemanticFilterExpression AtLeast<TChild>(Expression<Func<T, IReadOnlyList<TChild>>> relationship,
        int count, SemanticFilterExpression predicate) =>
        new SemanticAggregateFilter(Relationship(relationship), SemanticFilterAggregate.Count, null,
            SemanticAggregateFilterOperator.Gte, count, predicate);

    internal static FieldId Field<TValue>(Expression<Func<T, TValue>> field) =>
        FieldId.Create(Entity, MemberName(field));

    private static RelationshipId Relationship<TChild>(Expression<Func<T, IReadOnlyList<TChild>>> relationship) =>
        RelationshipId.Create(Entity, MemberName(relationship));

    internal static string MemberName<TValue>(Expression<Func<T, TValue>> selector) =>
        selector.Body switch
        {
            MemberExpression { Member.Name: var n } => n,
            UnaryExpression { Operand: MemberExpression { Member.Name: var n } } => n,
            _ => throw new ArgumentException("Select a single property, for example c => c.Status.", nameof(selector))
        };
}
