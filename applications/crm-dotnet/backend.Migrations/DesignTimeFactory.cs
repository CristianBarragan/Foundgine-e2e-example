using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Foundgine.Crm.Migrations;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CrmDbContext>
{
    public CrmDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Crm")
            ?? "Host=localhost;Port=5432;Database=crm;Username=crm;Password=crm-local-only";
        var options = new DbContextOptionsBuilder<CrmDbContext>().UseNpgsql(connection).Options;
        return new CrmDbContext(options);
    }
}
