using System.Net;
using System.Text.Json;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Webhooks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureAdmin.API.Tests;

public sealed class OutboundWebhookPublisherTests
{
    private const string SlackUrl = "https://hooks.slack.com/services/T000/B000/XXXX";

    [Fact]
    public async Task DraftTestPostsSampleWithUnsavedTemplate()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "ok");
        var publisher = CreatePublisher(handler);

        var result = await publisher.SendDraftTestAsync(
            new TestWebhookDraftRequest(
                WebhookDestinationKind.Slack,
                SlackUrl,
                TemplateMerged: "Merged {{pr_count}} for {{phase}}",
                Merged: true),
            CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(SlackUrl, handler.RequestUri?.ToString());
        using var doc = JsonDocument.Parse(handler.Body!);
        var text = doc.RootElement.GetProperty("text").GetString();
        Assert.StartsWith("Test notification from Azure Admin:", text);
        Assert.Contains("Merged 5 for dev → master", text);
    }

    [Fact]
    public async Task DraftTestReportsDestinationResponseBody()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, "invalid_payload");
        var publisher = CreatePublisher(handler);

        var result = await publisher.SendDraftTestAsync(
            new TestWebhookDraftRequest(WebhookDestinationKind.Slack, SlackUrl),
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("HTTP 400 Bad Request: invalid_payload", result.Error);
    }

    [Fact]
    public async Task DraftTestRejectsDisallowedHostWithoutSending()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "");
        var publisher = CreatePublisher(handler);

        var result = await publisher.SendDraftTestAsync(
            new TestWebhookDraftRequest(WebhookDestinationKind.Discord, SlackUrl),
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task DraftTestRequiresUrlOrExistingDestination()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "");
        var publisher = CreatePublisher(handler);

        var result = await publisher.SendDraftTestAsync(
            new TestWebhookDraftRequest(WebhookDestinationKind.Slack),
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("Webhook URL is required.", result.Error);
        Assert.Null(handler.RequestUri);
    }

    private static OutboundWebhookPublisher CreatePublisher(HttpMessageHandler handler)
    {
        // Never connects: draft tests with an explicit URL do not touch the database.
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .Options);
        var subscriptions = new WebhookSubscriptionService(db, new EphemeralDataProtectionProvider());
        return new OutboundWebhookPublisher(
            db,
            subscriptions,
            new SingleClientFactory(handler),
            new HttpContextAccessor(),
            NullLogger<OutboundWebhookPublisher>.Instance);
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }
}
