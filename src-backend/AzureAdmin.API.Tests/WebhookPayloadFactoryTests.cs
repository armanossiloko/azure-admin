using System.Text.Json;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Webhooks;

namespace AzureAdmin.API.Tests;

public sealed class WebhookPayloadFactoryTests
{
    [Fact]
    public void SlackConvertsMarkdownLinks()
    {
        var (_, body) = WebhookPayloadFactory.Create(
            WebhookDestinationKind.Slack,
            "Created 1 PR\n\nbackend:\n• [api](https://example/pr/1) (#1)");

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("text").GetString();
        Assert.Contains("<https://example/pr/1|api>", text);
        Assert.DoesNotContain("](", text);
    }

    [Fact]
    public void DiscordUsesContent()
    {
        var (_, body) = WebhookPayloadFactory.Create(
            WebhookDestinationKind.Discord,
            "hello");

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("hello", doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public void TeamsUsesPlainTextLikeDiscord()
    {
        var (_, body) = WebhookPayloadFactory.Create(
            WebhookDestinationKind.Teams,
            "Created 1 PR");

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Created 1 PR", doc.RootElement.GetProperty("text").GetString());
        Assert.False(doc.RootElement.TryGetProperty("attachments", out _));
    }
}
