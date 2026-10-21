using System.Net.Http;
using System.Security.Claims;
using System.Text;
using AzureAdmin.API.Configuration;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AzureAdmin.API.Services.Webhooks;

public sealed class OutboundWebhookPublisher
{
    public const string HttpClientName = "Webhooks";

    private readonly ApplicationDbContext _db;
    private readonly WebhookSubscriptionService _subscriptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptionsMonitor<AppOptions> _appOptions;
    private readonly ILogger<OutboundWebhookPublisher> _logger;

    /// <summary>Shown as <c>{{actor}}</c> when a message is triggered by the background status sync.</summary>
    internal const string BackgroundActor = "Automatic status sync";

    public OutboundWebhookPublisher(
        ApplicationDbContext db,
        WebhookSubscriptionService subscriptions,
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<AppOptions> appOptions,
        ILogger<OutboundWebhookPublisher> logger)
    {
        _appOptions = appOptions;
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

    public async Task<WebhookTestSendResult> SendTestAsync(
        Guid subscriptionId,
        bool merged,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetTrackedAsync(subscriptionId, cancellationToken);
        if (subscription is null)
            return WebhookTestSendResult.DestinationNotFound;

        return await DeliverAsync(subscription, BuildTestContext(merged), cancellationToken, isTest: true);
    }

    /// <summary>
    /// Sends a sample message using unsaved form values so a URL can be verified before it is saved.
    /// Delivery status is not recorded on any stored destination.
    /// </summary>
    public async Task<WebhookTestSendResult> SendDraftTestAsync(
        TestWebhookDraftRequest request,
        CancellationToken cancellationToken)
    {
        var kind = request.Kind;
        string url;
        if (!string.IsNullOrWhiteSpace(request.Url))
        {
            url = request.Url.Trim();
        }
        else if (request.ExistingId is { } existingId)
        {
            var existing = await _subscriptions.GetTrackedAsync(existingId, cancellationToken);
            if (existing is null)
                return WebhookTestSendResult.DestinationNotFound;

            // The kind of a saved destination cannot change, so its stored URL is only valid for that kind.
            kind = existing.Kind;
            try
            {
                url = _subscriptions.UnprotectUrl(existing.ProtectedUrl);
            }
            catch (InvalidOperationException ex)
            {
                return new WebhookTestSendResult(false, ex.Message);
            }
        }
        else
        {
            return new WebhookTestSendResult(false, "Webhook URL is required.");
        }

        if (!WebhookUrlRules.TryValidate(kind, url, out var urlError))
            return new WebhookTestSendResult(false, urlError);

        var stored = request.Merged ? request.TemplateMerged : request.TemplateCreated;
        if (stored is { Length: > WebhookSubscriptionService.MaxTemplateLength })
            return new WebhookTestSendResult(
                false,
                $"Message template must be at most {WebhookSubscriptionService.MaxTemplateLength} characters.");

        var text = RenderText(stored, BuildTestContext(request.Merged), isTest: true);
        return await PostAsync(kind, url, text, logId: null, cancellationToken);
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
        var text = RenderText(stored, context, isTest);

        var result = await PostAsync(subscription.Kind, url, text, subscription.Id, cancellationToken);
        await _subscriptions.RecordDeliveryAsync(subscription.Id, result.Ok, result.Error, cancellationToken);
        return result;
    }

    private async Task<WebhookTestSendResult> PostAsync(
        WebhookDestinationKind kind,
        string url,
        string text,
        Guid? logId,
        CancellationToken cancellationToken)
    {
        var (mediaType, body) = WebhookPayloadFactory.Create(kind, text);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var content = new StringContent(body, Encoding.UTF8, mediaType);
            using var response = await client.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                var responseText = await TryReadErrorBodyAsync(response, cancellationToken);
                if (!string.IsNullOrEmpty(responseText))
                    detail += $": {responseText}";
                _logger.LogWarning(
                    "Webhook {WebhookId} ({Kind}) returned {Status}.",
                    logId, kind, (int)response.StatusCode);
                return new WebhookTestSendResult(false, detail);
            }

            return new WebhookTestSendResult(true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Webhook {WebhookId} ({Kind}) request failed.", logId, kind);
            return new WebhookTestSendResult(false, ex.Message);
        }
    }

    private static async Task<string?> TryReadErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int maxLength = 300;
        try
        {
            var text = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (text.Length == 0)
                return null;
            return text.Length <= maxLength ? text : text[..maxLength] + "…";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string RenderText(string? storedTemplate, WebhookMessageContext context, bool isTest)
    {
        var template = WebhookMessageRenderer.ResolveTemplate(storedTemplate, context.Event);
        var text = WebhookMessageRenderer.Render(template, context, markdownLinks: true);
        return isTest ? "Test notification from Azure Admin:\n\n" + text : text;
    }

    private WebhookMessageContext BuildTestContext(bool merged) =>
        WebhookMessageRenderer.SampleContext with
        {
            Event = merged ? WebhookEventKind.DevToMasterMerged : WebhookEventKind.DevToMasterCreated,
            ReleaseUrl = TryGetReleaseUrl(Guid.Empty)?.Replace(Guid.Empty.ToString(), "example")
                ?? WebhookMessageRenderer.SampleContext.ReleaseUrl,
            Actor = TryGetActor() ?? WebhookMessageRenderer.SampleContext.Actor
        };

    private static WebhookPrLine ToLine(ReleasePullRequest pr)
    {
        var name = string.IsNullOrWhiteSpace(pr.RegisteredRepository.ServiceName)
            ? pr.RegisteredRepository.RepositoryIdOrName
            : pr.RegisteredRepository.ServiceName;
        return new WebhookPrLine(pr.Team.Name, name, pr.AzureDevOpsPullRequestId, pr.Url);
    }

    private string? TryGetReleaseUrl(Guid releaseId) =>
        BuildReleaseUrl(_appOptions.CurrentValue.PublicBaseUrl, _httpContextAccessor.HttpContext, releaseId);

    /// <summary>
    /// Link to the release page: the configured public base URL when set (also works without a request, e.g.
    /// the background status sync), otherwise derived from the current request; null when neither exists.
    /// </summary>
    internal static string? BuildReleaseUrl(string? publicBaseUrl, HttpContext? http, Guid releaseId)
    {
        if (Uri.TryCreate(publicBaseUrl?.Trim(), UriKind.Absolute, out var configured) &&
            (configured.Scheme == Uri.UriSchemeHttps || configured.Scheme == Uri.UriSchemeHttp))
            return $"{configured.GetLeftPart(UriPartial.Path).TrimEnd('/')}/releases/{releaseId}";

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
        var http = _httpContextAccessor.HttpContext;
        if (http is null)
            return BackgroundActor;

        var user = http.User;
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

public sealed record WebhookTestSendResult(bool Ok, string? Error, bool NotFound = false)
{
    public static WebhookTestSendResult DestinationNotFound { get; } = new(false, "Destination was not found.", NotFound: true);
}
