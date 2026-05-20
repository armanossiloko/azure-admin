using AzureAdmin.Api.Configuration;
using AzureAdmin.Api.Services.AzureDevOps;
using AzureAdmin.Api.Services.Identity;
using AzureAdmin.Api.Services.Releases;

namespace AzureAdmin.Api.DependencyInjection;

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
