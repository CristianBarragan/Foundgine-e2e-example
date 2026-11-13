using Crm.Api.Application;
using Foundgine.Providers.Tools.MCP;

namespace Crm.Api.Infrastructure;

/// <summary>
/// The CRM's Foundgine MCP server (<c>POST /mcp</c>, streamable HTTP, stateless). Only the read tools of the Foundgine MCP
/// adapter (<see cref="FoundgineMcpTools"/>: capability discovery and read-intent execution) are exposed. The mutation
/// tools (<c>FoundgineMcpMutationTools</c>) are deliberately NOT registered: agents read through MCP, and every write
/// stays behind the CRM API and the Temporal workflows.
/// Tenant and caller never come from MCP arguments. <see cref="FoundgineMcpTools"/> takes the host's
/// <c>ISecurityExecutionContextProvider</c> (the Keycloak provider registered by <c>AddCrmIdentity</c>), which builds the
/// context from the validated bearer token of the HTTP request; Foundgine fails closed when there is none.
/// </summary>
public static class McpExtensions
{
    public const string Route = "/mcp";

    public static bool McpEnabled(this IConfiguration configuration) => configuration.GetValue<bool>("Mcp:Enabled");

    public static IServiceCollection AddCrmMcp(this IServiceCollection services)
    {
        // Stateless: no MCP session to hijack, and the per-request scope (and so the per-request Keycloak context) is
        // what constructs FoundgineMcpTools (constructor injection of IFoundgine and ISecurityExecutionContextProvider).
        services.AddMcpServer(o => o.ServerInfo = new() { Name = "crm-foundgine", Version = "1.0.0" })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<FoundgineMcpTools>();
        return services;
    }

    /// <summary>Authenticated like the REST API, and limited to CRM readers (the same <c>crm.read</c> policy).</summary>
    public static IEndpointConventionBuilder MapCrmMcp(this WebApplication app) =>
        app.MapMcp(Route).RequireAuthorization(CrmRoles.ReadPolicy);
}
