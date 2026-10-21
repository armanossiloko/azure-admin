using AzureAdmin.API.Configuration;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Identity;
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
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            _logger.LogInformation("Pull request status sync is disabled (PullRequestSync:Enabled=false).");
            return;
        }

        _logger.LogInformation(
            "Pull request status sync enabled: every {Interval} min, first run in {Delay} s.",
            options.IntervalMinutes, options.InitialDelaySeconds);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.InitialDelaySeconds), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var summary = await RunOnceAsync(stoppingToken);
                    if (summary.ReleasesScanned > 0)
                        _logger.LogInformation(
                            "Pull request status sync: {Releases} release(s), {PullRequests} PR(s) checked, {Changed} status change(s), {Skipped} skipped (no PAT).",
                            summary.ReleasesScanned, summary.PullRequestsChecked, summary.StatusChanges, summary.SkippedNoPat);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pull request status sync run failed; will retry next interval.");
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

            // Byte-array length does not translate to SQL; the empty-PAT check happens client-side
            // (one small row per user and organization).
            holders = (await db.AzureDevOpsPatCredentials.AsNoTracking()
                    .Where(c => c.PatExpiresAt == null || c.PatExpiresAt > now)
                    .Select(c => new { c.UserId, c.OrganizationKey, c.UpdatedAt, c.ProtectedPat })
                    .ToListAsync(cancellationToken))
                .Where(c => c.ProtectedPat.Length > 0)
                .Select(c => new PatHolder(c.UserId, c.OrganizationKey, c.UpdatedAt))
                .ToList();
        }

        var summary = new SyncSummary();
        if (releaseIds.Count == 0)
            return summary;

        foreach (var releaseId in releaseIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var scope = _scopeFactory.CreateScope();
            var batch = scope.ServiceProvider.GetRequiredService<ReleasePullRequestBatchService>();
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

            // Anything on this path that still asks for the "current user" sees a real PAT holder
            // instead of throwing; the per-PR choice above is what actually talks to Azure DevOps.
            IReadOnlyList<ReleasePullRequestStatusResult> results;
            using (AmbientUser.Impersonate(holders.Count > 0 ? holders[0].UserId : Guid.Empty))
            {
                results = await batch.RefreshPullRequestStatusesAsync(
                    releaseId,
                    phase: null,
                    onlyActive: true,
                    userFor: UserFor,
                    maxConcurrency,
                    cancellationToken);
            }

            summary.ReleasesScanned++;
            summary.PullRequestsChecked += checkedCount;
            summary.SkippedNoPat += skipped;
            summary.StatusChanges += results.Count(r => before.TryGetValue(r.PullRequestId, out var prev) && prev != r.Status);
        }

        return summary;
    }

    public sealed class SyncSummary
    {
        public int ReleasesScanned { get; set; }
        public int PullRequestsChecked { get; set; }
        public int StatusChanges { get; set; }
        public int SkippedNoPat { get; set; }
    }
}
