using System.Net.Http;
using System.Security.Claims;
using System.Text;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Services.Webhooks;

public sealed class OutboundWebhookPublisher
{
    public const string HttpClientName = "Webhooks";

    private readonly ApplicationDbContext _db;
    private readonly WebhookSubscriptionService _subscriptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<OutboundWebhookPublisher> _logger;

    public OutboundWebhookPublisher(
        ApplicationDbContext db,
        WebhookSubscriptionService subscriptions,
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<OutboundWebhookPublisher> logger)
    {
        _db = db;
        _subscriptions = subscriptions;
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public Task NotifyPrBatchCreatedAsync(
        Guid releaseId,
        ReleasePrPhase phase,
        IReadOnlyCollection<Guid> pullRequestIds,
        CancellationToken cancellationToken)
    {
        var kind = WebhookEvents.From(phase, merged: false);
        return kind is null
            ? Task.CompletedTask
            : NotifyAsync(kind.Value, releaseId, phase, pullRequestIds, failed: [], cancellationToken);
    }

    public Task NotifyPrMergedAsync(
        Guid releaseId,
        ReleasePrPhase phase,
        IReadOnlyCollection<Guid> completedPullRequestIds,
        IReadOnlyList<WebhookPrLine> failed,
        CancellationToken cancellationToken)
    {
        var kind = WebhookEvents.From(phase, merged: true);
        return kind is null
            ? Task.CompletedTask
            : NotifyAsync(kind.Value, releaseId, phase, completedPullRequestIds, failed, cancellationToken);
    }

    public async Task<WebhookTestSendResult> SendTestAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetTrackedAsync(subscriptionId, cancellationToken);
        if (subscription is null)
            return new WebhookTestSendResult(false, "Destination was not found.");

        var sample = WebhookMessageRenderer.SampleContext with
        {
            ReleaseUrl = TryGetReleaseUrl(Guid.Empty)?.Replace(Guid.Empty.ToString(), "example")
                ?? WebhookMessageRenderer.SampleContext.ReleaseUrl,
            Actor = TryGetActor() ?? WebhookMessageRenderer.SampleContext.Actor
        };

        return await DeliverAsync(subscription, sample, cancellationToken, isTest: true);
    }

    private async Task NotifyAsync(
        WebhookEventKind eventKind,
        Guid releaseId,
        ReleasePrPhase phase,
        IReadOnlyCollection<Guid> pullRequestIds,
        IReadOnlyList<WebhookPrLine> failed,
        CancellationToken cancellationToken)
    {
        if (pullRequestIds.Count == 0 && failed.Count == 0)
            return;

        try
        {
            var destinations = await _subscriptions.ListEnabledForEventAsync(eventKind, cancellationToken);
            if (destinations.Count == 0)
                return;

            var release = await _db.Releases.AsNoTracking()
                .Where(r => r.Id == releaseId)
                .Select(r => new { r.Title, r.SprintLabel })
                .FirstOrDefaultAsync(cancellationToken);
            if (release is null)
                return;

            var prs = pullRequestIds.Count == 0
                ? []
                : await _db.ReleasePullRequests.AsNoTracking()
                    .Where(pr => pullRequestIds.Contains(pr.Id))
                    .Include(pr => pr.Team)
                    .Include(pr => pr.RegisteredRepository)
                    .ToListAsync(cancellationToken);

            var lines = prs
                .OrderBy(pr => pr.Team.Name)
                .ThenBy(pr => pr.RegisteredRepository.ServiceName ?? pr.RegisteredRepository.RepositoryIdOrName)
                .Select(ToLine)
                .ToList();

            if (lines.Count == 0 && failed.Count == 0)
                return;

            var context = new WebhookMessageContext(
                eventKind,
                release.Title,
                release.SprintLabel,
                WebhookMessageRenderer.PhaseLabel(phase),
                TryGetReleaseUrl(releaseId),
                TryGetActor(),
                lines,
                failed);

            foreach (var destination in destinations)
            {
                try
                {
                    await DeliverAsync(destination, context, cancellationToken, isTest: false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Webhook {WebhookId} ({Kind}) failed for {Event}.", destination.Id, destination.Kind, eventKind);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish {Event} webhooks for release {ReleaseId}.", eventKind, releaseId);
        }
    }

    private async Task<WebhookTestSendResult> DeliverAsync(
        WebhookSubscription subscription,
        WebhookMessageContext context,
        CancellationToken cancellationToken,
        bool isTest)
    {
        string url;
        try
        {
            url = _subscriptions.UnprotectUrl(subscription.ProtectedUrl);
        }
        catch (Exception ex)
        {
            await _subscriptions.RecordDeliveryAsync(subscription.Id, false, ex.Message, cancellationToken);
            return new WebhookTestSendResult(false, ex.Message);
        }

        if (!WebhookUrlRules.TryValidate(subscription.Kind, url, out var urlError))
        {
            await _subscriptions.RecordDeliveryAsync(subscription.Id, false, urlError, cancellationToken);
            return new WebhookTestSendResult(false, urlError);
        }

        var stored = WebhookEvents.IsMerged(context.Event)
            ? subscription.TemplateMerged
            : subscription.TemplateCreated;
        var template = WebhookMessageRenderer.ResolveTemplate(stored, context.Event);

        var text = WebhookMessageRenderer.Render(template, context, markdownLinks: true);
        if (isTest)
            text = "Test notification from Azure Admin:\n\n" + text;

        var (mediaType, body) = WebhookPayloadFactory.Create(subscription.Kind, text);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var content = new StringContent(body, Encoding.UTF8, mediaType);
            using var response = await client.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                await _subscriptions.RecordDeliveryAsync(subscription.Id, false, detail, cancellationToken);
                _logger.LogWarning(
                    "Webhook {WebhookId} ({Kind}) returned {Status}.",
                    subscription.Id, subscription.Kind, (int)response.StatusCode);
                return new WebhookTestSendResult(false, detail);
            }

            await _subscriptions.RecordDeliveryAsync(subscription.Id, true, null, cancellationToken);
            return new WebhookTestSendResult(true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            await _subscriptions.RecordDeliveryAsync(subscription.Id, false, ex.Message, cancellationToken);
            _logger.LogWarning(ex, "Webhook {WebhookId} ({Kind}) request failed.", subscription.Id, subscription.Kind);
            return new WebhookTestSendResult(false, ex.Message);
        }
    }

    private static WebhookPrLine ToLine(ReleasePullRequest pr)
    {
        var name = string.IsNullOrWhiteSpace(pr.RegisteredRepository.ServiceName)
            ? pr.RegisteredRepository.RepositoryIdOrName
            : pr.RegisteredRepository.ServiceName;
        return new WebhookPrLine(pr.Team.Name, name, pr.AzureDevOpsPullRequestId, pr.Url);
    }

    private string? TryGetReleaseUrl(Guid releaseId)
    {
        var http = _httpContextAccessor.HttpContext;
        if (http is null)
            return null;

        var request = http.Request;
        var scheme = FirstForwarded(request.Headers["X-Forwarded-Proto"]) ?? request.Scheme;
        var host = FirstForwarded(request.Headers["X-Forwarded-Host"]) ?? request.Host.ToUriComponent();
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var pathBase = request.PathBase.HasValue ? request.PathBase.Value!.TrimEnd('/') : "";
        return $"{scheme}://{host}{pathBase}/releases/{releaseId}";
    }

    private string? TryGetActor()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null)
            return null;
        return user.FindFirstValue("displayName")
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue(ClaimTypes.Name);
    }

    private static string? FirstForwarded(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;
        var first = header.Split(',')[0].Trim();
        return string.IsNullOrEmpty(first) ? null : first;
    }
}

public sealed record WebhookTestSendResult(bool Ok, string? Error);
