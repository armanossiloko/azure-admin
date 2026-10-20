using System.Net.Http;
using AzureAdmin.API.Configuration;
using AzureAdmin.API.Services.AzureDevOps;
using AzureAdmin.API.Services.Branding;
using AzureAdmin.API.Services.Git;
using AzureAdmin.API.Services.Health;
using AzureAdmin.API.Services.Identity;
using AzureAdmin.API.Services.Notifications;
using AzureAdmin.API.Services.Releases;
using AzureAdmin.API.Services.Webhooks;

namespace AzureAdmin.API.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureDevOpsOptions>(configuration.GetSection("AzureDevOps"));
        services.Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.SectionName));
        services.Configure<BrandingOptions>(configuration.GetSection(BrandingOptions.SectionName));
        services.AddOptions<PostgresOptions>()
            .Bind(configuration.GetSection(PostgresOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IBrandingResolver, BrandingResolver>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<KeycloakUserProvisioner>();
        services.AddScoped<AzureDevOpsOrganizationService>();
        services.AddScoped<AzureDevOpsPatCredentialService>();
        services.AddScoped<IAzureDevOpsPatResolver, AzureDevOpsPatResolver>();
        services.AddScoped<AzureDevOpsClient>();
        services.AddScoped<AzureDevOpsCatalogService>();
        services.AddScoped<OrganizationScopeService>();
        services.AddScoped<StaleBranchService>();
        services.AddScoped<ReleaseCommitNotesService>();
        services.AddScoped<ReleasePullRequestBatchService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<WebhookSubscriptionService>();
        services.AddScoped<OutboundWebhookPublisher>();
        services.AddHttpClient(OutboundWebhookPublisher.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AzureAdmin-Webhooks/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });
        services.AddSingleton<ConventionalCommitParser>();
        services.AddSingleton<JiraReferenceExtractor>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database");

        return services;
    }
}
