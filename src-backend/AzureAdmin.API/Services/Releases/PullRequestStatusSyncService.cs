using AzureAdmin.API.Configuration;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AzureAdmin.API.Services.Releases;

/// <summary>
/// Periodically checks Azure DevOps for every still-active pull request on Draft/Active releases and
/// updates the stored status, so merged PRs show up (and merge notifications, phase-complete chat
/// messages, and release promotion fire) without anyone opening the release page.
/// Configured via <see cref="PullRequestSyncOptions"/>; one scope per release keeps DbContext tracking small.
/// </summary>
public sealed class PullRequestStatusSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<PullRequestSyncOptions> _options;
    private readonly ILogger<PullRequestStatusSyncService> _logger;

    public PullRequestStatusSyncService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<PullRequestSyncOptions> options,
        ILogger<PullRequestStatusSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initial = _options.CurrentValue;
        _logger.LogInformation(
            "Pull request status sync {State}: every {Interval} min, first check in {Delay} s.",
            initial.Enabled ? "enabled" : "disabled", initial.IntervalMinutes, initial.InitialDelaySeconds);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(initial.InitialDelaySeconds), stoppingToken);

            var wasEnabled = initial.Enabled;
            while (!stoppingToken.IsCancellationRequested)
            {
                // Read every interval so PullRequestSync:Enabled can be flipped without a restart.
                var options = _options.CurrentValue;
                if (options.Enabled != wasEnabled)
                {
                    _logger.LogInformation("Pull request status sync {State}.", options.Enabled ? "enabled" : "disabled");
                    wasEnabled = options.Enabled;
                }

                if (options.Enabled)
                {
                    try
                    {
                        var summary = await RunOnceAsync(stoppingToken);
                        if (summary.ReleasesScanned > 0)
                            _logger.LogInformation(
                                "Pull request status sync: {Releases} release(s), {PullRequests} PR(s) checked, {Changed} status change(s), {Skipped} skipped (no PAT), {Failed} release(s) failed.",
                                summary.ReleasesScanned, summary.PullRequestsChecked, summary.StatusChanges, summary.SkippedNoPat, summary.ReleasesFailed);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Pull request status sync run failed; will retry next interval.");
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(_options.CurrentValue.IntervalMinutes), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutting down.
        }
    }

    /// <summary>One full pass over every release that still has active pull requests.</summary>
    public async Task<SyncSummary> RunOnceAsync(CancellationToken cancellationToken)
    {
        var maxConcurrency = _options.CurrentValue.MaxConcurrency;
        List<Guid> releaseIds;
        List<PatHolder> holders;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTimeOffset.UtcNow;

            releaseIds = await db.ReleasePullRequests.AsNoTracking()
                .Where(pr => pr.Status == ReleasePullRequestStatus.Active &&
                             (pr.Release.Status == ReleaseLifecycleStatus.Draft ||
                              pr.Release.Status == ReleaseLifecycleStatus.Active))
                .Select(pr => pr.ReleaseId)
                .Distinct()
                .ToListAsync(cancellationToken);

            // Only who holds an unexpired PAT for which organization; the encrypted PAT itself is resolved
            // later, once per user and organization actually used. Saving a PAT rejects empty values.
            holders = await db.AzureDevOpsPatCredentials.AsNoTracking()
                .Where(c => c.PatExpiresAt == null || c.PatExpiresAt > now)
                .Select(c => new PatHolder(c.UserId, c.OrganizationKey, c.UpdatedAt))
                .ToListAsync(cancellationToken);
        }

        var summary = new SyncSummary();
        if (releaseIds.Count == 0)
            return summary;

        foreach (var releaseId in releaseIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // One broken release (bad data, a database error) must not stop the others until the next interval.
            try
            {
                await SyncReleaseAsync(releaseId, holders, maxConcurrency, summary, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                summary.ReleasesFailed++;
                _logger.LogError(ex, "Pull request status sync failed for release {ReleaseId}; continuing with the next release.", releaseId);
            }
        }

        return summary;
    }

    private async Task SyncReleaseAsync(
        Guid releaseId,
        IReadOnlyList<PatHolder> holders,
        int maxConcurrency,
        SyncSummary summary,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var batch = scope.ServiceProvider.GetRequiredService<IPullRequestStatusRefresher>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var before = await db.ReleasePullRequests.AsNoTracking()
            .Where(pr => pr.ReleaseId == releaseId)
            .ToDictionaryAsync(pr => pr.Id, pr => pr.Status, cancellationToken);

        var skipped = 0;
        var checkedCount = 0;

        Guid? UserFor(ReleasePullRequest pr)
        {
            var user = PullRequestSyncUserPicker.Pick(pr.CreatedByUserId, pr.RegisteredRepository.AzureDevOpsOrganization, holders);
            if (user is null)
                skipped++;
            else
                checkedCount++;
            return user;
        }

        // No HTTP request and no impersonation here: the PAT comes from UserFor, and merge notifications go
        // to the PR/release creator (fallback null), so nothing on this path needs a "current user".
        var results = await batch.RefreshPullRequestStatusesAsync(
            releaseId,
            phase: null,
            onlyActive: true,
            userFor: UserFor,
            fallbackNotificationRecipient: null,
            maxConcurrency,
            cancellationToken);

        summary.ReleasesScanned++;
        summary.PullRequestsChecked += checkedCount;
        summary.SkippedNoPat += skipped;
        summary.StatusChanges += results.Count(r => before.TryGetValue(r.PullRequestId, out var prev) && prev != r.Status);
    }

    public sealed class SyncSummary
    {
        public int ReleasesScanned { get; set; }
        public int PullRequestsChecked { get; set; }
        public int StatusChanges { get; set; }
        public int SkippedNoPat { get; set; }
        public int ReleasesFailed { get; set; }
    }
}
