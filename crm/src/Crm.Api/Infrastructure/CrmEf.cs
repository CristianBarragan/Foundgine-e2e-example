using Crm.Api.Domain;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Crm.Api.Infrastructure;

/// <summary>
/// The one place that configures EF Core for the CRM: the Foundgine-derived model plus <see cref="CrmStorage"/>,
/// Npgsql, and the migrations assembly. Used by the running app and by the design-time factory (dotnet ef).
/// </summary>
public static class CrmEf
{
    public static void ConfigureModel(FoundgineEfOptions options) => options.Customize(CrmStorage.Apply);

    public static DbContextOptionsBuilder UseCrmNpgsql(this DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(CrmEf).Assembly.GetName().Name))
            // The snapshot is maintained next to the model; the diff check is advisory here (see tools/README).
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
}

/// <summary>Lets `dotnet ef migrations add/script` build the context without starting the web host.</summary>
public sealed class CrmDesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<FoundgineDbContext>
{
    public FoundgineDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(CrmDomain.ConnectionStringName)
                         ?? "Host=localhost;Database=crm;Username=crm;Password=crm";
        var options = new DbContextOptionsBuilder<FoundgineDbContext>().UseCrmNpgsql(connection).Options;
        var efOptions = new FoundgineEfOptions();
        CrmEf.ConfigureModel(efOptions);
        return new FoundgineDbContext((DbContextOptions<FoundgineDbContext>)options, new FoundgineEfModel(CrmSemantics.Metadata, efOptions));
    }
}
