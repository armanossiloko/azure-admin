using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AzureAdmin.API.Data;

/// <summary>
/// Lets <c>dotnet ef migrations</c> construct the context without appsettings / Keycloak / Postgres options.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=azureadmin;Username=azureadmin;Password=design-time")
            .Options;
        return new ApplicationDbContext(options);
    }
}
