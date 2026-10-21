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
using Microsoft.Extensions.Http.Resilience;
using Polly;

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
        services.AddOptions<PullRequestSyncOptions>()
            .Bind(configuration.GetSection(PullRequestSyncOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IBrandingResolver, BrandingResolver>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<KeycloakUserProvisioner>();
        services.AddScoped<AzureDevOpsOrganizationService>();
        services.AddScoped<AzureDevOpsPatCredentialService>();
        services.AddScoped<IAzureDevOpsPatResolver, AzureDevOpsPatResolver>();
        services.AddScoped<AzureDevOpsClient>();
        AddAzureDevOpsHttpClients(services);
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
        services.AddHostedService<PullRequestStatusSyncService>();
        services.AddSingleton<ConventionalCommitParser>();
        services.AddSingleton<JiraReferenceExtractor>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database");

        return services;
    }

    /// <summary>
    /// Two named clients for dev.azure.com. Reads get the standard pipeline (retry with backoff on
    /// 5xx/408/429 honouring Retry-After, attempt + total timeouts, circuit breaker). Writes are only
    /// retried on 429/503, which Azure DevOps returns before processing the request, so a retried
    /// create never opens a duplicate pull request.
    /// </summary>
    private static void AddAzureDevOpsHttpClients(IServiceCollection services)
    {
        services.AddHttpClient(AzureDevOpsClient.ReadHttpClientName)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                // Must be at least twice the attempt timeout.
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            });

        services.AddHttpClient(AzureDevOpsClient.WriteHttpClientName)
            .AddResilienceHandler("azure-devops-write", builder =>
            {
                builder
                    .AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        Delay = TimeSpan.FromSeconds(2),
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        ShouldRetryAfterHeader = true,
                        ShouldHandle = args => ValueTask.FromResult(
                            args.Outcome.Result?.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                                or System.Net.HttpStatusCode.ServiceUnavailable)
                    })
                    .AddTimeout(TimeSpan.FromSeconds(60));
            });
    }
}
