using Crm.Api.Infrastructure;
using Foundgine.Extensions.Temporal.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Temporalio.Client;
using Temporalio.Extensions.OpenTelemetry;
using Xunit;

public class TemporalTracingTests
{
    private static TemporalClientConnectOptions Options(bool withTracing)
    {
        var services = new ServiceCollection();
        services.AddFoundgineTemporal(o => { o.Address = "localhost:7233"; o.Namespace = "enterprise"; });
        if (withTracing) services.AddFoundgineTemporalTracing();
        using var provider = services.BuildServiceProvider();
        // Reading the options does not connect: the Foundgine client is lazy.
        return provider.GetRequiredService<IOptions<TemporalClientConnectOptions>>().Value;
    }

    [Fact]
    public void Foundgine_client_options_carry_the_tracing_interceptor()
    {
        var options = Options(withTracing: true);
        Assert.Contains(options.Interceptors ?? [], i => i is TracingInterceptor);
    }

    [Fact]
    public void Foundgine_client_keeps_its_address_and_namespace()
    {
        var options = Options(withTracing: true);
        Assert.Equal("localhost:7233", options.TargetHost);
        Assert.Equal("enterprise", options.Namespace);
    }

    [Fact]
    public void Without_the_extension_the_client_has_no_interceptor()
    {
        // Documents the gap this closes: Foundgine adds none itself.
        Assert.DoesNotContain(Options(withTracing: false).Interceptors ?? [], i => i is TracingInterceptor);
    }
}
