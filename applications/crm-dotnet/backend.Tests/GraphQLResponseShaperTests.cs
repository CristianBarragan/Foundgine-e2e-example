using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic;
using Foundgine.Crm.Api.GraphQL;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Xunit;

namespace Foundgine.Crm.Semantic.Tests;

public sealed class GraphQLResponseShaperTests
{
    [Fact]
    public void Shape_projects_scalar_fields_and_preserves_field_aliases()
    {
        var companyId = new EntityId(1);
        var id = new FieldId(1);
        var name = new FieldId(2);
        var model = new SemanticModelBuilder()
            .Entity(companyId, "Company", entity => entity
                .Identity(id, "Id")
                .Field(name, "Name", typeof(string)))
            .Build()
            .Freeze();

        var execution = new GraphQLQueryExecutionResult(
            new ExecutionResult([
                new ExecutionRow(
                    new Dictionary<string, object?>(),
                    new Dictionary<ExecutionCellKey, object?>
                    {
                        [new ExecutionCellKey(0, companyId, id)] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        [new ExecutionCellKey(0, companyId, name)] = "Acme"
                    })
            ]),
            new GraphQLResultShape([
                new GraphQLResultField(id, null, "id", "companyId"),
                new GraphQLResultField(name, null, "name", "displayName")
            ]));

        var response = Assert.IsType<Dictionary<string, object?>>(
            GraphQLResponseShaper.Shape(model, companyId, execution));
        var data = Assert.IsType<Dictionary<string, object?>>(response["data"]);
        var rows = Assert.IsType<Dictionary<string, object?>[]>(data["company"]);
        Assert.Equal("Acme", rows[0]["displayName"]);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), rows[0]["companyId"]);
    }

    [Fact]
    public void Shape_rejects_relationship_selections_until_nested_materialization_is_available()
    {
        var companyId = new EntityId(1);
        var contactId = new EntityId(2);
        var relationshipId = new RelationshipId(1);
        var model = new SemanticModelBuilder()
            .Entity(companyId, "Company", entity => entity
                .Identity(new FieldId(1), "Id")
                .Relationship(relationshipId, "Contacts", contactId, RelationshipCardinality.Many))
            .Entity(contactId, "Contact", entity => entity.Identity(new FieldId(2), "Id"))
            .Build()
            .Freeze();
        var execution = new GraphQLQueryExecutionResult(
            new ExecutionResult([]),
            new GraphQLResultShape([
                new GraphQLResultField(null, relationshipId, "contacts", "contacts",
                    new GraphQLResultShape([]))
            ]));

        Assert.Throws<NotSupportedException>(() =>
            GraphQLResponseShaper.Shape(model, companyId, execution));
    }
}
