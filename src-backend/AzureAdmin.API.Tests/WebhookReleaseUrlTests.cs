using AzureAdmin.API.Services.Webhooks;
using Microsoft.AspNetCore.Http;

namespace AzureAdmin.API.Tests;

public sealed class WebhookReleaseUrlTests
{
    private static readonly Guid ReleaseId = Guid.Parse("6b1c2a6e-0e5b-4a51-9b5e-3d0f1f1e2a10");

    [Fact]
    public void Background_sync_without_a_request_uses_the_configured_public_url()
    {
        var url = OutboundWebhookPublisher.BuildReleaseUrl("https://admin.example.com/", http: null, ReleaseId);

        Assert.Equal($"https://admin.example.com/releases/{ReleaseId}", url);
    }

    [Fact]
    public void Configured_url_with_a_path_base_is_kept()
    {
        var url = OutboundWebhookPublisher.BuildReleaseUrl("https://example.com/azure-admin", http: null, ReleaseId);

        Assert.Equal($"https://example.com/azure-admin/releases/{ReleaseId}", url);
    }

    [Fact]
    public void Without_configuration_or_a_request_there_is_no_link()
    {
        Assert.Null(OutboundWebhookPublisher.BuildReleaseUrl(null, http: null, ReleaseId));
        Assert.Null(OutboundWebhookPublisher.BuildReleaseUrl("not a url", http: null, ReleaseId));
    }

    [Fact]
    public void Without_configuration_the_request_host_is_used()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("admin.internal");

        var url = OutboundWebhookPublisher.BuildReleaseUrl(null, http, ReleaseId);

        Assert.Equal($"https://admin.internal/releases/{ReleaseId}", url);
    }
}
