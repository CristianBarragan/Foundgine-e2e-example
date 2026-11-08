using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using Temporalio.Extensions.OpenTelemetry;

namespace Crm.Api.Infrastructure;

public static class TemporalTracingExtensions
{
    /// <summary>
    /// Puts the Temporal <see cref="TracingInterceptor"/> on the client that <c>AddFoundgineTemporal</c> registers.
    /// Foundgine's options have no interceptor setting, but it builds that client with <c>AddTemporalClient(address, ns)</c>,
    /// which reads the default (unnamed) <see cref="TemporalClientConnectOptions"/>. Configuring those options from the host
    /// adds the interceptor without changing Foundgine. It is a no-op when OpenTelemetry is off, like the interceptor on
    /// <c>TemporalWorkflowStarter</c>. Appends to any interceptors already set rather than replacing them.
    /// </summary>
    public static IServiceCollection AddFoundgineTemporalTracing(this IServiceCollection services) =>
        services.Configure<TemporalClientConnectOptions>(o =>
            o.Interceptors = [.. o.Interceptors ?? [], new TracingInterceptor()]);
}
