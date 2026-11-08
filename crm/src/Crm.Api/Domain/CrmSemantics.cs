using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Providers.Aot;

namespace Crm.Api.Domain;

/// <summary>Typed handles (entity/field/relationship ids) checked against the compiled registry at first use.</summary>
public static class CrmSemantics
{
    public static readonly MetadataRegistry Metadata = new CrmModel().Build();

    private static EntityId EntityOf(string name)
    {
        var id = EntityId.Create(name);
        if (!Metadata.TryGet(id, out _))
            throw new InvalidOperationException($"Semantic entity '{name}' is not part of CrmModel.");
        return id;
    }

    private static GeneratedSemanticField FieldOf(EntityId entity, string entityName, string member)
    {
        var id = FieldId.Create(entityName, member);
        if (!Metadata.Get(entity).EffectiveFields.Any(f => f.Id == id))
            throw new InvalidOperationException($"Field '{entityName}.{member}' is not mapped in CrmModel.");
        return new GeneratedSemanticField(entity, id, member);
    }

    private static IReadOnlyList<FieldId> AllFieldsOf(EntityId entity) =>
        Metadata.Get(entity).EffectiveFields.Select(f => f.Id).ToArray();

    public static class Customer
    {
        private const string N = "Customer";
        public static readonly EntityId Entity = EntityOf(N);
        public static readonly GeneratedSemanticField Id = FieldOf(Entity, N, "Id");
        public static readonly GeneratedSemanticField TenantId = FieldOf(Entity, N, "TenantId");
        public static readonly GeneratedSemanticField Kind = FieldOf(Entity, N, "Kind");
        public static readonly GeneratedSemanticField Name = FieldOf(Entity, N, "Name");
        public static readonly GeneratedSemanticField Email = FieldOf(Entity, N, "Email");
        public static readonly GeneratedSemanticField Status = FieldOf(Entity, N, "Status");
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);
    }

    public static class Contact
    {
        private const string N = "Contact";
        public static readonly EntityId Entity = EntityOf(N);
        public static readonly GeneratedSemanticField Id = FieldOf(Entity, N, "Id");
        public static readonly GeneratedSemanticField TenantId = FieldOf(Entity, N, "TenantId");
        public static readonly GeneratedSemanticField CustomerId = FieldOf(Entity, N, "CustomerId");
        public static readonly GeneratedSemanticField LastName = FieldOf(Entity, N, "LastName");
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);
    }

    public static class Opportunity
    {
        private const string N = "Opportunity";
        public static readonly EntityId Entity = EntityOf(N);
        public static readonly GeneratedSemanticField Id = FieldOf(Entity, N, "Id");
        public static readonly GeneratedSemanticField TenantId = FieldOf(Entity, N, "TenantId");
        public static readonly GeneratedSemanticField CustomerId = FieldOf(Entity, N, "CustomerId");
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);
    }

    public static class Quote
    {
        private const string N = "Quote";
        public static readonly EntityId Entity = EntityOf(N);
        public static readonly GeneratedSemanticField Id = FieldOf(Entity, N, "Id");
        public static readonly GeneratedSemanticField TenantId = FieldOf(Entity, N, "TenantId");
        public static readonly GeneratedSemanticField CustomerId = FieldOf(Entity, N, "CustomerId");
        public static readonly GeneratedSemanticField Status = FieldOf(Entity, N, "Status");
        public static IReadOnlyList<FieldId> All { get; } = AllFieldsOf(Entity);
    }
}
