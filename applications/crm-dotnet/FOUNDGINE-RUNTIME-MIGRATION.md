# Foundgine Runtime migration

## Source and package identity

This migration targets the supplied `Foundgine-main.zip`. Its `Directory.Build.props` sets
`VersionPrefix` to `2.2.1`. The API references `Foundgine.Core`, `Foundgine.Runtime`, and
`Foundgine.Providers`, all at `2.2.1`.

The supplied `foundgine.extensions.2.2.1.nupkg` is package ID `Foundgine.Extensions`.
Its nuspec describes the Hot Chocolate GraphQL integration and its dependencies are
`Foundgine.Core`, `Foundgine.Runtime`, and `HotChocolate.Language`. It is not a PostgreSQL
persistence extension. This CRM is REST-based, so the package is intentionally not referenced.

## Runtime composition

`Program.cs` registers:

- `SqlCompiler` as `IProviderPlanCompiler`;
- `PooledCrmSqlExecutionProvider` as `IExecutionProvider`, delegating execution to
  `Foundgine.Providers.Storage.Sql.SqlExecutionProvider`;
- `PooledCrmPostgresMutationProvider` as `IMutationBatchExecutionProvider`, delegating to
  `Foundgine.Providers.Storage.Sql.Mutation.Postgres.PostgresBatchedMutationExecutionProvider`;
- `AddFoundgine(...)` with the generated/overlaid CRM semantic model, authorization policy,
  generated metadata as `IMutationSchema`, and the PostgreSQL mutation provider.

The mutation adapter also implements `IMutationSecurityConformanceEvaluator` and delegates the
proof to the concrete PostgreSQL provider while reporting the adapter identity expected by
Foundgine Runtime's execution certificate.

`CrmSemanticExecutor` now translates REST-level CRM operations into `SemanticRequest` and
`SemanticMutationRequest`, then invokes `IFoundgineExecutor` and `IFoundgineMutations`.
It no longer constructs `SemanticAuthorizer`, `Planner`, `SemanticMutationPlanner`,
`MutationAuthorizer`, or `SemanticMutationExecutionLowerer` itself.

## Tenant authorization

Foundgine Runtime holds its policy on the singleton engine. `CrmRuntimeAuthorizationPolicy`
bridges the validated `CrmScope` through `AsyncLocal`, whose values flow with each async request.
This prevents one request's tenant or roles from being shared with another concurrent request.

The Runtime read and mutation engines are eagerly constructed during application startup while
a temporary bootstrap scope is available for their immutable capability/security contract
discovery. `SealBootstrap()` is then called. After that point, a Foundgine authorization call
without a host-established request scope throws `UnauthorizedAccessException`.

Read predicates bind `context.TenantId` through the Foundgine `ExecutionContext`. Creates reject
caller-supplied `TenantId` and stamp the value from the validated host scope. As before, any
future update/delete implementation must explicitly constrain writes to the authenticated tenant;
the read predicate is not automatically applied to mutation operations.

## Validation status

Static checks performed on this archive:
- C# project XML, JSON and XML configuration parsing;
- Docker Compose YAML parsing;
- deployment script shell syntax;
- Foundgine package version alignment;
- no remaining manual Foundgine authorization/planning/mutation-lowering orchestration in
  `backend/`.

Not performed: `dotnet restore`, `dotnet build`, xUnit execution, EF migration execution,
PostgreSQL integration tests, Docker builds, Terraform validation/plan, Keycloak login tests,
or Native AOT publishing. The environment used to prepare this archive has no .NET SDK,
Terraform, or Docker executable. Do not treat static checks as compilation or deployment proof.
