using Foundgine.Providers.Outbox;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Providers.Storage.Sql;
using Foundgine.Runtime;
using Foundgine.Runtime.Capabilities;
using Foundgine.Core.Semantic.Resolution;
using Foundgine.Core.Semantic.Security.Warrants;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Crm.Api;
using Foundgine.Crm.Api.Foundgine;
using Foundgine.Crm.Api.GraphQL;
using Foundgine.Extensions.GraphQL.HotChocolate;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ---- Persistence + Foundgine ------------------------------------------------------------------
// Runtime owns semantic authorization/planning/execution orchestration. Foundgine.Providers
// supplies SQL compilation/execution and PostgreSQL mutation execution. NpgsqlDataSource is
// only the pooled connection factory; EF Core remains isolated in backend.Migrations.
var crmConnectionString = builder.Configuration.GetConnectionString("Crm")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Crm.");
var crmDataSource = NpgsqlDataSource.Create(crmConnectionString);
builder.Services.AddSingleton(crmDataSource);
builder.Services.AddSingleton(CrmModel.Metadata);
builder.Services.AddSingleton<IMetadataProvider>(CrmModel.Metadata);
builder.Services.AddSingleton<IProviderPlanCompiler>(_ => new SqlCompiler(CrmModel.Metadata));
builder.Services.AddSingleton<IExecutionProvider>(_ => new PooledCrmSqlExecutionProvider(crmDataSource));
var crmAuthorizationPolicy = new CrmRuntimeAuthorizationPolicy();
builder.Services.AddSingleton(crmAuthorizationPolicy);
// The built-in contract-backed source enables bounded lexical grounding without
// adding an external retrieval dependency. Foundgine remains authoritative for
// semantic validation, authorization, planning and provider execution.
builder.Services.AddSingleton<ISemanticLexicalCandidateSource, SemanticContractLexicalCandidateSource>();
var httpContextAccessor = new HttpContextAccessor();
builder.Services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
// Keycloak validation + warrant issuance come from Foundgine.Extensions (see AuthSetup / CrmKeycloak).
var keycloak = builder.AddCrmKeycloak(httpContextAccessor);
builder.Services.AddFoundgine(options =>
{
    options.Model = CrmModel.Model;
    options.AuthorizationPolicy = crmAuthorizationPolicy;
    options.MutationSchema = CrmModel.Metadata;
    options.MutationProvider = new PooledCrmPostgresMutationProvider(crmDataSource, CrmModel.Metadata);
    options.UseGrounding();
    options.WarrantKeyResolver = keycloak.KeyResolver;
    options.ExpectedWarrantIssuer = keycloak.Options.WarrantIssuer;
    options.WarrantReplayStore = new MemorySecurityWarrantReplayStore();
});
// MCP and REST dynamic execution share the same Foundgine semantic execution adapter.
// Tenant context is read from the validated Keycloak principal, never tool arguments.
// Already registered as ISecurityExecutionContextProvider by AddCrmKeycloak.
var crmSecurityContextProvider = keycloak.SecurityContextProvider;
builder.Services.AddSingleton<Func<ExecutionContext>>(() =>
{
    var principal = httpContextAccessor.HttpContext?.User
        ?? throw new UnauthorizedAccessException("Authenticated HTTP context is required.");
    var scope = CrmScope.From(principal);
    return new ExecutionContext(new Dictionary<string, object?>
    {
        [CrmAuthorization.TenantContextKey] = scope.TenantId
    });
});
builder.Services.AddSingleton<HotChocolateSemanticAdapter>(_ => new HotChocolateSemanticAdapter(CrmModel.Model));
builder.Services.AddSingleton<FoundgineHotChocolateQueryExecutor>(services => new FoundgineHotChocolateQueryExecutor(
    services.GetRequiredService<IFoundgine>(),
    services.GetRequiredService<HotChocolateSemanticAdapter>(),
    services.GetRequiredService<Foundgine.Core.Semantic.Security.Execution.ISecurityExecutionContextProvider>(),
    services.GetRequiredService<Func<ExecutionContext>>()));

builder.Services.AddFoundgineMcp(
    contextFactory: () =>
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new UnauthorizedAccessException("Authenticated HTTP context is required.");
        var scope = CrmScope.From(principal);
        return new ExecutionContext(new Dictionary<string, object?>
        {
            [CrmAuthorization.TenantContextKey] = scope.TenantId
        });
    },
    securityContextProvider: crmSecurityContextProvider);
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<FoundgineMcpTools>();

builder.Services.AddHttpClient<CrmAiAssistant>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddSingleton<CrmSemanticExecutor>();
builder.Services.AddScoped<ICrmStore, FoundgineCrmStore>();
// PostgreSQL outbox (foundgine_outbox) as writer; CommercialOrderService enlists it in its own transaction.
builder.Services.AddFoundgineGatewayPostgresOutbox();
builder.Services.AddSingleton<CommercialOrderService>();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, CrmJsonContext.Default));
builder.AddCrmAuthorizationPolicies();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:3000").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options => options.AddPolicy("ai-chat", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        })));

var app = builder.Build();

// Force both singleton Runtime engines to construct while the bootstrap policy
// is temporarily available for their immutable security-contract discovery.
// Once constructed, all authorization calls require an async-flow-local scope
// established from the validated Keycloak principal.
_ = app.Services.GetRequiredService<IFoundgineExecutor>();
_ = app.Services.GetRequiredService<IFoundgineMutations>();
crmAuthorizationPolicy.SealBootstrap();

// Map failures to safe responses; never return exception details to the client.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, message) = error switch
    {
        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden."),
        CrmValidationException e => (StatusCodes.Status400BadRequest, e.Message),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error.")
    };
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new ApiError(message), CrmJsonContext.Default.ApiError);
}));

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// The Runtime authorization policy is singleton but resolves the validated CRM
// identity from an async-flow-local request scope. Establish it for both transports.
app.Use(async (context, next) =>
{
    var isDynamicSemanticRequest = context.Request.Path.StartsWithSegments("/mcp") ||
                                   context.Request.Path.StartsWithSegments("/graphql") ||
                                   context.Request.Path.StartsWithSegments("/api/semantic/execute") ||
                                   context.Request.Path.StartsWithSegments("/api/assistant/chat");
    if (isDynamicSemanticRequest && context.User.Identity?.IsAuthenticated == true)
    {
        using var requestScope = CrmAuthorizationRequestContext.Push(CrmScope.From(context.User));
        await next();
        return;
    }

    await next();
});

app.MapGet("/health", () => Results.Ok(new HealthResponse("healthy")));

// Dynamic REST execution: accepts either natural-language intent or structured
// Foundgine read-intent JSON. The same FoundgineMcpTools adapter backs the MCP tool.
// Read-only AI assistant. The model can invoke only crm_query, which delegates to the
// existing Foundgine MCP tool; request identity and tenant remain host-derived.
app.MapPost("/api/assistant/chat", async (
    AssistantChatRequest request,
    CrmAiAssistant assistant,
    FoundgineMcpTools foundgine,
    CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await assistant.ChatAsync(request, foundgine, ct));
    }
    catch (AiNotConfiguredException)
    {
        return Results.Json(new ApiError("The AI assistant is not configured. Set Ai__ApiKey on the API service."), CrmJsonContext.Default.ApiError, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (AiProviderException)
    {
        return Results.Json(new ApiError("The AI service could not complete the request."), CrmJsonContext.Default.ApiError, statusCode: StatusCodes.Status502BadGateway);
    }
}).RequireAuthorization("crm-read").RequireRateLimiting("ai-chat");

app.MapPost("/api/semantic/execute", async (
    SemanticExecuteRequest request,
    FoundgineMcpTools executor,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Intent))
        return Results.BadRequest(new ApiError("Intent is required."));

    var json = await executor.ExecuteQueryAsync(request.Intent, ct);
    return Results.Content(json, "application/json");
}).RequireAuthorization("crm-read");

// MCP endpoint exposes the same semantic execution contract as REST.
app.MapMcp("/mcp").RequireAuthorization("crm-read");

// Dynamic GraphQL-over-HTTP endpoint. Foundgine.Extensions supplies the built-in
// Hot Chocolate parser/semantic adapter and secure query executor. Tenant and
// warrant context are derived exclusively from the validated Keycloak principal.
// The current response materializer supports scalar root selections; nested
// relationship selections fail explicitly rather than returning misleading data.
app.MapPost("/graphql", async (
    GraphQLHttpRequest request,
    HotChocolateSemanticAdapter adapter,
    FoundgineHotChocolateQueryExecutor executor,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Query))
        return Results.BadRequest(new ApiError("GraphQL query is required."));

    var variables = request.Variables?.ToDictionary(
        pair => pair.Key,
        pair => (object?)pair.Value,
        StringComparer.Ordinal);
    try
    {
        var adaptation = adapter.AdaptResultShape(request.Query, variables, request.OperationName);
        var execution = await executor.ExecuteAsync(request.Query, variables, request.OperationName, ct);
        var response = GraphQLResponseShaper.Shape(CrmModel.Model, adaptation.Request.Root, execution);
        return Results.Json(response, CrmJsonContext.Default.Object);
    }
    catch (NotSupportedException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
}).RequireAuthorization("crm-read");

// Semantic discovery is deliberately schema-only. It never returns tenant data or acts as an
// authorization grant; every data request still crosses CrmSemanticExecutor.
app.MapGet("/api/semantic/capabilities", () =>
    Results.Ok(CrmSemanticCatalog.Describe())).RequireAuthorization("crm-read");

// Canonical Foundgine capability discovery is evaluated against the same semantic model
// and per-request authorization policy used by execution. Conditional tenant predicates
// are represented as conditional access, never serialized as executable client policy.
app.MapGet("/api/semantic/capability-contract", (ClaimsPrincipal user, IFoundgine foundgine) =>
{
    var scope = CrmScope.From(user);
    using var requestScope = CrmAuthorizationRequestContext.Push(scope);
    return Results.Ok(CrmSemanticCatalog.DescribeContract(foundgine.DescribeCapabilityContract()));
}).RequireAuthorization("crm-read");

app.MapGet("/api/companies", async (ClaimsPrincipal user, ICrmStore store, CancellationToken ct) =>
    Results.Ok(await store.GetCompaniesAsync(CrmScope.From(user), ct))).RequireAuthorization("crm-read");
app.MapPost("/api/companies", async (CreateCompany input, ClaimsPrincipal user, ICrmStore store, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Name)) return Results.BadRequest(new ApiError("Company name is required."));
    var item = await store.CreateCompanyAsync(CrmScope.From(user), input, ct);
    return Results.Created($"/api/companies/{item.Id}", item);
}).RequireAuthorization("crm-write");

app.MapGet("/api/contacts", async (ClaimsPrincipal user, ICrmStore store, CancellationToken ct) =>
    Results.Ok(await store.GetContactsAsync(CrmScope.From(user), ct))).RequireAuthorization("crm-read");
app.MapPost("/api/contacts", async (CreateContact input, ClaimsPrincipal user, ICrmStore store, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.FirstName) || string.IsNullOrWhiteSpace(input.LastName))
        return Results.BadRequest(new ApiError("First and last name are required."));
    return Results.Ok(await store.CreateContactAsync(CrmScope.From(user), input, ct));
}).RequireAuthorization("crm-write");

app.MapGet("/api/opportunities", async (ClaimsPrincipal user, ICrmStore store, CancellationToken ct) =>
    Results.Ok(await store.GetOpportunitiesAsync(CrmScope.From(user), ct))).RequireAuthorization("crm-read");
app.MapPost("/api/opportunities", async (CreateOpportunity input, ClaimsPrincipal user, ICrmStore store, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Name)) return Results.BadRequest(new ApiError("Opportunity name is required."));
    return Results.Ok(await store.CreateOpportunityAsync(CrmScope.From(user), input, ct));
}).RequireAuthorization("crm-write");

// ---- Commercial orders (CRM-owned aggregate) ------------------------------------------------------
// Acceptance and the CommercialOrderAccepted outbox row commit in ONE transaction; the separate
// Foundgine.Crm.OutboxDispatcher starts the Temporal fulfillment workflow from that row.
// NOTE: these routes use CommercialOrderService (explicit tenant-scoped SQL) rather than the Foundgine
// semantic executor; exposing commercial orders through the semantic model is a later increment.
app.MapPost("/api/commercial-orders", async (
    CreateCommercialOrder input,
    HttpRequest http,
    ClaimsPrincipal user,
    CommercialOrderService orders,
    CancellationToken ct) =>
{
    try
    {
        var (order, replayed) = await orders.CreateAsync(
            CrmScope.From(user), http.Headers["Idempotency-Key"].FirstOrDefault(), input, ct);
        return replayed
            ? Results.Ok(order)
            : Results.Created($"/api/commercial-orders/{order.Id}", order);
    }
    catch (CommercialOrderIdempotencyConflictException exception)
    {
        return Results.Json(new ApiError(exception.Message), CrmJsonContext.Default.ApiError, statusCode: StatusCodes.Status409Conflict);
    }
}).RequireAuthorization("crm-write");

app.MapGet("/api/commercial-orders", async (ClaimsPrincipal user, CommercialOrderService orders, CancellationToken ct) =>
    Results.Ok(await orders.ListAsync(CrmScope.From(user), 100, ct))).RequireAuthorization("crm-read");

app.MapGet("/api/commercial-orders/{id:guid}", async (Guid id, ClaimsPrincipal user, CommercialOrderService orders, CancellationToken ct) =>
    await orders.GetAsync(CrmScope.From(user), id, ct) is { } order
        ? Results.Ok(order)
        : Results.NotFound(new ApiError("Commercial order not found."))).RequireAuthorization("crm-read");

// ---- Orchestration callbacks (service identity only) ----------------------------------------------
// Caller is the Temporal worker holding the 'orchestration-service' realm role. It is a platform
// identity without a per-tenant claim, so the tenant comes from the command body; the order must exist
// under that exact (tenant, id) pair or the call is a 404. Commands are de-duplicated by IdempotencyKey.
static IResult CallbackResult(CallbackOutcome outcome) => outcome switch
{
    CallbackOutcome.Applied or CallbackOutcome.Replayed => Results.NoContent(),
    CallbackOutcome.NotFound => Results.NotFound(new ApiError("Commercial order not found.")),
    _ => Results.Json(new ApiError("Callback conflicts with the order's current state or a prior command."),
        CrmJsonContext.Default.ApiError, statusCode: StatusCodes.Status409Conflict)
};

app.MapPost("/api/integration/commercial-orders/{id:guid}/fulfillment-confirmed", async (
    Guid id, FulfillmentCallback body, CommercialOrderService orders, CancellationToken ct) =>
    CallbackResult(await orders.ApplyFulfillmentAsync(
        id, CommercialOrderValidation.ValidateCallback(id, body), CommercialOrderStatus.FulfillmentConfirmed, ct)))
    .RequireAuthorization("crm-integration");

app.MapPost("/api/integration/commercial-orders/{id:guid}/fulfillment-failed", async (
    Guid id, FulfillmentCallback body, CommercialOrderService orders, CancellationToken ct) =>
    CallbackResult(await orders.ApplyFulfillmentAsync(
        id, CommercialOrderValidation.ValidateCallback(id, body), CommercialOrderStatus.FulfillmentFailed, ct)))
    .RequireAuthorization("crm-integration");

app.Run();
