package com.enterprise.erp.foundgine;

import com.enterprise.erp.security.ErpCaller;
import com.foundgine.core.abstractions.AuthorizationOperation;
import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.semantic.authorization.ConfiguredSemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.ISemanticAuthorizationPolicy;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationConfiguration;
import com.foundgine.core.semantic.authorization.SemanticAuthorizationContext;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Binds the validated ERP JWT caller to Foundgine's semantic authorization pipeline.
 * Writes remain in the ERP application services, which own their transactions.
 */
public final class ErpAuthorization implements FoundgineOrm.Authorization<ErpCaller> {
    static final String TENANT_ATTRIBUTE = "tenantId";
    static final String TENANT_CONTEXT_KEY = "context.tenantId";

    private static final Set<String> ADMIN = Set.of("erp-admin", "enterprise-admin");
    private static final Map<String, Set<String>> ENTITY_ROLES = Map.of(
            "Customer", Set.of("erp-sales", "erp-finance", "erp-admin", "enterprise-admin"),
            "SalesOrder", Set.of("erp-sales", "erp-finance", "erp-admin", "enterprise-admin"),
            "Return", Set.of("erp-sales", "erp-inventory", "erp-admin", "enterprise-admin"),
            "Shipment", Set.of("erp-sales", "erp-inventory", "erp-admin", "enterprise-admin"),
            "Invoice", Set.of("erp-finance", "erp-admin", "enterprise-admin"),
            "Payment", Set.of("erp-finance", "erp-admin", "enterprise-admin"));
    private static final Set<String> INTERNAL_FIELDS = Set.of("CorrelationId", "IdempotencyKey");

    @Override
    public ISemanticAuthorizationPolicy policy(ErpCaller caller, MetadataRegistry metadata) {
        String tenant = caller.requireTenant();

        Map<EntityId, String> entityNames = new HashMap<>();
        Set<FieldId> internalFields = new HashSet<>();
        for (EntityMetadata entity : metadata.entities()) {
            entityNames.put(entity.entityId(), entity.name());
            for (var field : entity.effectiveFields()) {
                if (INTERNAL_FIELDS.contains(field.name())) internalFields.add(field.id());
            }
        }

        Map<RelationshipId, EntityId> relationshipTargets = new HashMap<>();
        for (RelationshipMetadata relationship : metadata.relationships()) {
            relationshipTargets.put(relationship.id(), relationship.target());
        }

        var configuration = new SemanticAuthorizationConfiguration()
                .addEntityRule((context, entity, operation) ->
                        operation == AuthorizationOperation.READ
                                && mayRead(caller, entityNames.get(entity)))
                .addFieldRule((context, entity, field, operation) ->
                        operation == AuthorizationOperation.READ
                                && (!internalFields.contains(field) || isAdmin(caller)))
                .addRelationshipRule((context, entity, relationship, operation) -> {
                    if (operation != AuthorizationOperation.READ) return false;
                    EntityId target = relationshipTargets.get(relationship);
                    return target != null && mayRead(caller, entityNames.get(target));
                })
                .addPredicateRule((context, entity, operation) ->
                        operation == AuthorizationOperation.READ ? tenantPredicate() : null);

        return new ConfiguredSemanticAuthorizationPolicy(
                configuration,
                new SemanticAuthorizationContext(tenant, String.join(",", caller.roles()), Map.of()));
    }

    @Override
    public ExecutionContext executionContext(ErpCaller caller) {
        return new ExecutionContext(Map.of(TENANT_CONTEXT_KEY, caller.requireTenant()));
    }

    private static boolean mayRead(ErpCaller caller, String entityName) {
        Set<String> allowedRoles = entityName == null ? null : ENTITY_ROLES.get(entityName);
        return allowedRoles != null && hasAny(caller.roles(), allowedRoles);
    }

    private static boolean isAdmin(ErpCaller caller) {
        return hasAny(caller.roles(), ADMIN);
    }

    private static boolean hasAny(List<String> roles, Set<String> allowed) {
        for (String role : roles) if (allowed.contains(role)) return true;
        return false;
    }

    private static AuthorizationPredicate tenantPredicate() {
        return AuthorizationPredicate.equal(
                AuthorizationPredicate.member(
                        AuthorizationPredicate.resourceParameter("resource"), TENANT_ATTRIBUTE),
                AuthorizationPredicate.member(
                        AuthorizationPredicate.contextParameter("context"), "tenantId"));
    }
}
