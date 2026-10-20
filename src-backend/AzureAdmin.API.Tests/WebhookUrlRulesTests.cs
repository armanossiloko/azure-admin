using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Webhooks;

namespace AzureAdmin.API.Tests;

public sealed class WebhookUrlRulesTests
{
    [Theory]
    [InlineData(WebhookDestinationKind.Slack, "https://hooks.slack.com/services/T000/B000/XXX")]
    [InlineData(WebhookDestinationKind.Discord, "https://discord.com/api/webhooks/1/abc")]
    [InlineData(WebhookDestinationKind.Discord, "https://discordapp.com/api/webhooks/1/abc")]
    [InlineData(WebhookDestinationKind.Teams, "https://prod-00.westus.logic.azure.com:443/workflows/abc/triggers/manual/paths/invoke")]
    [InlineData(WebhookDestinationKind.Teams, "https://webhook.office.com/webhookb2/guid")]
    [InlineData(WebhookDestinationKind.Teams, "https://default.environment.api.powerplatform.com/powerautomate/automations/direct/workflows/abc/triggers/manual/paths/invoke")]
    public void AcceptsKnownHosts(WebhookDestinationKind kind, string url)
    {
        Assert.True(WebhookUrlRules.TryValidate(kind, url, out var error), error);
    }

    [Theory]
    [InlineData(WebhookDestinationKind.Slack, "https://example.com/hooks")]
    [InlineData(WebhookDestinationKind.Slack, "http://hooks.slack.com/services/T000/B000/XXX")]
    [InlineData(WebhookDestinationKind.Discord, "https://discord.com/api/channels/1")]
    [InlineData(WebhookDestinationKind.Teams, "https://169.254.169.254/latest/meta-data")]
    [InlineData(WebhookDestinationKind.Teams, "https://evil.logic.azure.com.attacker.example/x")]
    public void RejectsUnknownOrUnsafeUrls(WebhookDestinationKind kind, string url)
    {
        Assert.False(WebhookUrlRules.TryValidate(kind, url, out _));
    }

    [Fact]
    public void MaskHidesSecretPathSegments()
    {
        var masked = WebhookUrlRules.Mask("https://hooks.slack.com/services/T00000000/B00000000/secret");
        Assert.StartsWith("https://hooks.slack.com/", masked);
        Assert.DoesNotContain("secret", masked);
        Assert.Contains("••••", masked);
    }
}
