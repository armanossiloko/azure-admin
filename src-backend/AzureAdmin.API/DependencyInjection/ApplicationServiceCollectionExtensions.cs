using AzureAdmin.API.Configuration;
using AzureAdmin.API.Services.AzureDevOps;
using AzureAdmin.API.Services.Identity;
using AzureAdmin.API.Services.Releases;

namespace AzureAdmin.API.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureDevOpsOptions>(configuration.GetSection("AzureDevOps"));
        services.Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.SectionName));
        services.Configure<PostgresOptions>(configuration.GetSection(PostgresOptions.SectionName));

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<AzureDevOpsOrganizationService>();
        services.AddScoped<AzureDevOpsPatCredentialService>();
        services.AddScoped<IAzureDevOpsPatResolver, AzureDevOpsPatResolver>();
        services.AddScoped<AzureDevOpsClient>();
        services.AddScoped<AzureDevOpsCatalogService>();
        services.AddScoped<ReleaseCommitNotesService>();
        services.AddScoped<ReleasePullRequestBatchService>();

        return services;
    }
}
