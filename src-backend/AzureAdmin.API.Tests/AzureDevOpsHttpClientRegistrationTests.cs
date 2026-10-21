using AzureAdmin.API.DependencyInjection;
using AzureAdmin.API.Services.AzureDevOps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AzureAdmin.API.Tests;

/// <summary>
/// The resilience pipelines validate their options the first time a client is created (for example the
/// circuit breaker sampling window must be at least twice the attempt timeout). Creating both named
/// clients here catches a bad tuning change at test time instead of on the first Azure DevOps call.
/// </summary>
public class AzureDevOpsHttpClientRegistrationTests
{
    private static ServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Postgres:Host"] = "localhost",
                ["Postgres:Database"] = "db",
                ["Postgres:Username"] = "u",
                ["Postgres:Password"] = "p",
                ["AzureDevOps:ApiVersion"] = "7.1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddApplicationServices(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Read_client_pipeline_options_are_valid()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(AzureDevOpsClient.ReadHttpClientName);

        Assert.NotNull(client);
    }

    [Fact]
    public void Write_client_pipeline_options_are_valid()
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(AzureDevOpsClient.WriteHttpClientName);

        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(AzureDevOpsClient.ReadHttpClientName)]
    [InlineData(AzureDevOpsClient.WriteHttpClientName)]
    public void HttpClient_timeout_is_left_to_the_resilience_pipeline(string name)
    {
        using var provider = Build();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var client = factory.CreateClient(name);

        // The 100 s default would end requests before the pipeline's own total timeout.
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }
}
