using System.Net.Http;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.AzureDevOps;
using AzureAdmin.API.Services.Identity;
using AzureAdmin.API.Services.Notifications;
using AzureAdmin.API.Services.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Services.Releases;

public sealed class ReleasePullRequestBatchService
{
    private readonly ApplicationDbContext _db;
    private readonly AzureDevOpsClient _ado;
    private readonly ICurrentUser _currentUser;
    private readonly ReleaseCommitNotesService _commitNotes;
    private readonly NotificationService _notifications;
    private readonly OutboundWebhookPublisher _webhooks;
    private readonly IAzureDevOpsPatResolver _patResolver;
    private readonly ILogger<ReleasePullRequestBatchService> _logger;

    public ReleasePullRequestBatchService(
        ApplicationDbContext db,
        AzureDevOpsClient ado,
        ICurrentUser currentUser,
        ReleaseCommitNotesService commitNotes,
        NotificationService notifications,
        OutboundWebhookPublisher webhooks,
        IAzureDevOpsPatResolver patResolver,
        ILogger<ReleasePullRequestBatchService> logger)
    {
        _db = db;
        _logger = logger;
        _ado = ado;
        _currentUser = currentUser;
        _patResolver = patResolver;
        _commitNotes = commitNotes;
        _notifications = notifications;
        _webhooks = webhooks;
    }

    public async Task<BatchPreflightResponse> PreflightPullRequestsForReleaseAsync(
        Guid releaseId,
        Guid teamId,
        BatchCreateReleasePullRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var releaseExists = await _db.Releases.AnyAsync(r => r.Id == releaseId, cancellationToken);
        if (!releaseExists)
            throw new InvalidOperationException("Release was not found.");

        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, cancellationToken);
        if (!teamExists)
            throw new InvalidOperationException("Team was not found.");

        request = SanitizeBranchOverrides(request);

        var (activeRepos, skippedDecommissioned) = await LoadAndValidateReposAsync(teamId, request, cancellationToken);
        var userId = _currentUser.GetRequiredUserId();
        var sourceBranch = ResolveSourceBranch(request);
        var targetBranch = ResolveTargetBranch(request);

        var results = new List<BatchPreflightRepoResult>();
        foreach (var repo in activeRepos)
        {
            var issues = new List<string>();

            try
            {
                var refs = await _ado.ListBranchRefsAsync(
                    userId,
                    repo.AzureDevOpsOrganization,
                    repo.AzureDevOpsProject,
                    repo.RepositoryIdOrName,
                    cancellationToken);

                var branchNames = refs
                    .Select(r => BranchShortName(r.Name) ?? r.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (!branchNames.Contains(BranchShortName(sourceBranch) ?? sourceBranch))
                    issues.Add($"Source branch '{BranchShortName(sourceBranch) ?? sourceBranch}' was not found.");
                if (!branchNames.Contains(BranchShortName(targetBranch) ?? targetBranch))
                    issues.Add($"Target branch '{BranchShortName(targetBranch) ?? targetBranch}' was not found.");
            }
            catch (HttpRequestException ex)
            {
                issues.Add($"Could not list branches (check PAT for '{repo.AzureDevOpsOrganization}'): {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                issues.Add(ex.Message);
            }

            var notices = new List<string>();
            var drift = await GetBranchDriftAsync(userId, repo, cancellationToken);
            if (drift.Error is not null)
                issues.Add(drift.Error);
            else if (request.Phase == ReleasePrPhase.ProdToDev)
            {
                if (!drift.ProdAhead)
                {
                    issues.Add(
                        "prod is not ahead of dev — there is nothing to back-merge. " +
                        "Use Dev → Master or Master → Prod once neither branch is ahead of dev.");
                }
                else
                {
                    var reason = DescribeDrift(drift);
                    var backMergeIssue = await GetProdToDevBlockerAsync(releaseId, repo, reason, cancellationToken);
                    if (backMergeIssue is not null)
                        issues.Add(backMergeIssue);
                }
            }
            else if (drift.ProdAhead)
            {
                var reason = DescribeDrift(drift);
                var backMergeIssue = await GetProdToDevBlockerAsync(releaseId, repo, reason, cancellationToken);
                if (backMergeIssue is not null)
                    issues.Add(backMergeIssue);
                else
                {
                    notices.Add(
                        $"{reason}. Select Prod → Dev (or continue — this batch will open prod → dev instead of {PromotionLabel(request.Phase)}). " +
                        "Dev → master and master → prod stay closed until prod is not ahead of dev.");
                }
            }
            else if (drift.MasterAhead)
            {
                issues.Add(
                    "master is ahead of dev, and prod is not. Neither master nor prod may contain commits that are not in dev " +
                    "before a release promotion. Merge those master commits into dev before opening Dev → Master or Master → Prod.");
            }
            else
            {
                if (request.Phase == ReleasePrPhase.MasterToProd)
                {
                    var gateIssue = await GetMasterToProdGateIssueAsync(userId, releaseId, repo, cancellationToken);
                    if (gateIssue is not null)
                        issues.Add(gateIssue);
                }

                var existing = await _db.ReleasePullRequests.AsNoTracking()
                    .FirstOrDefaultAsync(
                        pr => pr.ReleaseId == releaseId
                              && pr.RegisteredRepositoryId == repo.Id
                              && pr.Phase == request.Phase,
                        cancellationToken);

                if (existing is not null
                    && existing.Status is not (ReleasePullRequestStatus.Abandoned or ReleasePullRequestStatus.Unknown))
                {
                    // Unknown may still be abandoned in ADO; create path cleans that up. Active/Completed block.
                    if (existing.Status is ReleasePullRequestStatus.Active or ReleasePullRequestStatus.Completed)
                    {
                        issues.Add(
                            $"A {existing.Status.ToString().ToLowerInvariant()} pull request for this phase already exists " +
                            $"(#{existing.AzureDevOpsPullRequestId}).");
                    }
                }
            }

            results.Add(new BatchPreflightRepoResult(
                repo.Id,
                repo.RepositoryIdOrName,
                repo.ServiceName,
                issues.Count == 0,
                issues,
                notices));
        }

        return new BatchPreflightResponse(
            results.Count > 0 && results.All(r => r.Ok),
            results,
            skippedDecommissioned);
    }

    public async Task<BatchCreateReleasePullRequestsResponse> CreatePullRequestsForReleaseAsync(
        Guid releaseId,
        Guid teamId,
        BatchCreateReleasePullRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var release = await _db.Releases.AsNoTracking()
            .Where(r => r.Id == releaseId)
            .Select(r => new { r.SprintLabel, r.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (release is null)
            throw new InvalidOperationException("Release was not found.");

        if (release.Status is ReleaseLifecycleStatus.Completed or ReleaseLifecycleStatus.Archived)
            throw new InvalidOperationException("This release is closed; no further pull requests can be created.");

        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, cancellationToken);
        if (!teamExists)
            throw new InvalidOperationException("Team was not found.");

        await EnsureTeamEnrolledAsync(releaseId, teamId, cancellationToken);

        request = SanitizeBranchOverrides(request);

        var (activeRepos, skippedDecommissioned) = await LoadAndValidateReposAsync(teamId, request, cancellationToken);

        var userId = _currentUser.GetRequiredUserId();
        var sourceBranch = ResolveSourceBranch(request);
        var targetBranch = ResolveTargetBranch(request);
        var pullRequestTitle = BuildPullRequestTitle(request.Phase, release.SprintLabel);

        var driftByRepoId = new Dictionary<Guid, BranchDrift>();
        foreach (var repo in activeRepos)
        {
            var drift = await GetBranchDriftAsync(userId, repo, cancellationToken);
            if (drift.Error is not null)
                throw new InvalidOperationException($"{repo.RepositoryIdOrName}: {drift.Error}");
            driftByRepoId[repo.Id] = drift;
        }

        if (request.Phase == ReleasePrPhase.ProdToDev)
        {
            var notAhead = activeRepos
                .Where(r => !driftByRepoId[r.Id].ProdAhead)
                .Select(r => r.RepositoryIdOrName)
                .ToList();
            if (notAhead.Count > 0)
            {
                throw new InvalidOperationException(
                    $"prod is not ahead of dev in {string.Join(", ", notAhead)}. " +
                    "Prod → Dev is only for repositories where prod has commits that are not in dev.");
            }

            await RemoveAbandonedOrMissingPullRequestRowsAsync(
                userId,
                releaseId,
                ReleasePrPhase.ProdToDev,
                activeRepos.Select(r => r.Id).ToList(),
                cancellationToken);

            var results = new List<CreatedPullRequestResult>();
            foreach (var repo in activeRepos)
            {
                results.Add(await CreateStoredPullRequestAsync(
                    userId,
                    releaseId,
                    teamId,
                    repo,
                    ReleasePrPhase.ProdToDev,
                    sourceBranch,
                    targetBranch,
                    pullRequestTitle,
                    request.Description,
                    note: null,
                    cancellationToken));
            }

            await PromoteReleaseToActiveIfDraftAsync(releaseId, cancellationToken);
            return new BatchCreateReleasePullRequestsResponse(results, skippedDecommissioned);
        }

        var masterBlocked = activeRepos
            .Where(r => driftByRepoId[r.Id].MasterAhead && !driftByRepoId[r.Id].ProdAhead)
            .Select(r => r.RepositoryIdOrName)
            .ToList();
        if (masterBlocked.Count > 0)
        {
            throw new InvalidOperationException(
                $"master is ahead of dev in {string.Join(", ", masterBlocked)}. " +
                "Neither master nor prod may be ahead of dev before a release promotion, and prod is not ahead of dev there. " +
                "Merge those commits into dev before opening Dev → Master or Master → Prod.");
        }

        var forwardRepos = activeRepos.Where(r => !driftByRepoId[r.Id].ProdAhead).ToList();
        var backMergeRepos = activeRepos.Where(r => driftByRepoId[r.Id].ProdAhead).ToList();

        if (request.Phase == ReleasePrPhase.MasterToProd)
        {
            foreach (var repo in forwardRepos)
            {
                var gateIssue = await GetMasterToProdGateIssueAsync(userId, releaseId, repo, cancellationToken);
                if (gateIssue is not null)
                    throw new InvalidOperationException($"{repo.RepositoryIdOrName}: {gateIssue}");
            }
        }

        if (forwardRepos.Count > 0)
        {
            await RemoveAbandonedOrMissingPullRequestRowsAsync(
                userId,
                releaseId,
                request.Phase,
                forwardRepos.Select(r => r.Id).ToList(),
                cancellationToken);
        }

        if (backMergeRepos.Count > 0)
        {
            await RemoveAbandonedOrMissingPullRequestRowsAsync(
                userId,
                releaseId,
                ReleasePrPhase.ProdToDev,
                backMergeRepos.Select(r => r.Id).ToList(),
                cancellationToken);
        }

        var created = new List<CreatedPullRequestResult>();
        foreach (var repo in forwardRepos)
        {
            created.Add(await CreateStoredPullRequestAsync(
                userId,
                releaseId,
                teamId,
                repo,
                request.Phase,
                sourceBranch,
                targetBranch,
                pullRequestTitle,
                request.Description,
                note: null,
                cancellationToken));
        }

        if (backMergeRepos.Count > 0)
        {
            var backMergeTitle = BuildPullRequestTitle(ReleasePrPhase.ProdToDev, release.SprintLabel);
            var promotionLabel = PromotionLabel(request.Phase);
            foreach (var repo in backMergeRepos)
            {
                var reason = DescribeDrift(driftByRepoId[repo.Id]);
                var note =
                    $"{reason}. Opened prod → dev instead of {promotionLabel}. " +
                    "Release promotions stay closed until prod is not ahead of dev. Merge this pull request, then run the promotion again.";
                created.Add(await CreateStoredPullRequestAsync(
                    userId,
                    releaseId,
                    teamId,
                    repo,
                    ReleasePrPhase.ProdToDev,
                    sourceBranch: "prod",
                    targetBranch: "dev",
                    backMergeTitle,
                    BuildBackMergeDescription(note, request.Description),
                    note,
                    cancellationToken));
            }
        }

        await PromoteReleaseToActiveIfDraftAsync(releaseId, cancellationToken);
        return new BatchCreateReleasePullRequestsResponse(created, skippedDecommissioned);
    }

    public async Task<MultiTeamBatchCreateReleasePullRequestsResponse> CreatePullRequestsForMultipleTeamsAsync(
        Guid releaseId,
        MultiTeamBatchCreateReleasePullRequestsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Teams is null || request.Teams.Count == 0)
            throw new InvalidOperationException("Select at least one team.");

        var created = new List<CreatedPullRequestResult>();
        var skipped = new List<string>();
        var teamErrors = new List<string>();

        foreach (var teamBatch in request.Teams)
        {
            if (teamBatch.RegisteredRepositoryIds is null || teamBatch.RegisteredRepositoryIds.Count == 0)
                continue;

            var teamName = await _db.Teams.AsNoTracking()
                .Where(t => t.Id == teamBatch.TeamId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(cancellationToken) ?? teamBatch.TeamId.ToString();

            var single = new BatchCreateReleasePullRequestsRequest(
                request.Phase,
                request.Title,
                request.Description,
                request.SourceBranch,
                request.TargetBranch,
                teamBatch.RegisteredRepositoryIds);

            try
            {
                var result = await CreatePullRequestsForReleaseAsync(
                    releaseId, teamBatch.TeamId, single, cancellationToken);
                created.AddRange(result.Results);
                skipped.AddRange(result.SkippedDecommissionedRepositories);
            }
            catch (InvalidOperationException ex)
            {
                teamErrors.Add($"{teamName}: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                teamErrors.Add($"{teamName}: {ex.Message}");
            }
        }

        var createdIds = created.Where(r => r.Id is { } id && id != Guid.Empty).Select(r => r.Id!.Value).ToList();
        if (createdIds.Count > 0)
            await _webhooks.NotifyPrBatchCreatedAsync(releaseId, request.Phase, createdIds, cancellationToken);

        return new MultiTeamBatchCreateReleasePullRequestsResponse(created, skipped, teamErrors);
    }

    private async Task<(List<RegisteredRepository> Active, List<string> SkippedDecommissioned)> LoadAndValidateReposAsync(
        Guid teamId,
        BatchCreateReleasePullRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var repoIds = request.RegisteredRepositoryIds.Distinct().ToList();
        if (repoIds.Count == 0)
            throw new InvalidOperationException("Select at least one registered repository.");

        var repos = await _db.RegisteredRepositories
            .Where(r => repoIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        if (repos.Count != repoIds.Count)
        {
            var missing = repoIds.Except(repos.Select(r => r.Id)).ToList();
            throw new InvalidOperationException($"Unknown repository id(s): {string.Join(", ", missing)}.");
        }

        foreach (var repo in repos)
        {
            if (repo.TeamId != teamId)
            {
                throw new InvalidOperationException(
                    $"Repository '{repo.RepositoryIdOrName}' is assigned to another team, not the team selected for this action.");
            }
        }

        var activeRepos = repos.Where(r => !r.IsDecommissioned).ToList();
        var skippedDecommissioned = repos
            .Where(r => r.IsDecommissioned)
            .Select(r => string.IsNullOrWhiteSpace(r.ServiceName) ? r.RepositoryIdOrName : r.ServiceName!)
            .ToList();

        if (activeRepos.Count == 0)
        {
            throw new InvalidOperationException(
                "Every selected repository is decommissioned, so no pull requests were created. Restore a repository before including it in a release.");
        }

        return (activeRepos, skippedDecommissioned);
    }

    /// <summary>
    /// Neither master nor prod may contain commits that dev does not have.
    /// If prod is ahead, the only allowed pull request is prod → dev.
    /// The dev → master / master → prod cycle runs only after prod is not ahead of dev.
    /// </summary>
    private async Task<BranchDrift> GetBranchDriftAsync(
        Guid userId,
        RegisteredRepository repo,
        CancellationToken cancellationToken)
    {
        var master = await TryCountCommitsAheadAsync(userId, repo, "master", "dev", cancellationToken);
        var prod = await TryCountCommitsAheadAsync(userId, repo, "prod", "dev", cancellationToken);

        var errors = new[] { master.Error, prod.Error }
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();
        if (errors.Count > 0)
            return new BranchDrift(false, false, string.Join(" ", errors));

        return new BranchDrift(prod.Ahead > 0, master.Ahead > 0, null);
    }

    private static string DescribeDrift(BranchDrift drift)
    {
        if (drift.ProdAhead && drift.MasterAhead)
            return "prod and master are ahead of dev";
        if (drift.ProdAhead)
            return "prod is ahead of dev";
        return "master is ahead of dev";
    }

    /// <summary>
    /// Whether <paramref name="branch"/> has any commit that <paramref name="baseBranch"/> does not.
    /// A missing branch counts as not ahead.
    /// </summary>
    private async Task<(int Ahead, string? Error)> TryCountCommitsAheadAsync(
        Guid userId,
        RegisteredRepository repo,
        string branch,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        try
        {
            var commits = await _ado.GetCommitsBetweenBranchesAsync(
                userId,
                repo.AzureDevOpsOrganization,
                repo.AzureDevOpsProject,
                repo.RepositoryIdOrName,
                branch,
                baseBranch,
                top: 1,
                cancellationToken);
            return (commits.Count, null);
        }
        catch (HttpRequestException ex) when (IsUnresolvedBranch(ex))
        {
            return (0, null);
        }
        catch (HttpRequestException ex)
        {
            return (0, $"Could not check whether {branch} is ahead of {baseBranch}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return (0, $"Could not check whether {branch} is ahead of {baseBranch}: {ex.Message}");
        }
    }

    private static bool IsUnresolvedBranch(HttpRequestException ex) =>
        ex.Data[AzureDevOpsHttp.TypeKeyDataKey] is "GitUnresolvableToCommitException"
        || ex.Message.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("GitUnresolvableToCommitException", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An active or completed prod → dev pull request blocks another back-merge while prod is still ahead of dev.
    /// </summary>
    private async Task<string?> GetProdToDevBlockerAsync(
        Guid releaseId,
        RegisteredRepository repo,
        string driftReason,
        CancellationToken cancellationToken)
    {
        var existing = await _db.ReleasePullRequests.AsNoTracking()
            .FirstOrDefaultAsync(
                pr => pr.ReleaseId == releaseId
                      && pr.RegisteredRepositoryId == repo.Id
                      && pr.Phase == ReleasePrPhase.ProdToDev,
                cancellationToken);

        if (existing is null)
            return null;

        if (existing.Status == ReleasePullRequestStatus.Completed)
        {
            return
                $"Prod → dev pull request #{existing.AzureDevOpsPullRequestId} is completed, but {driftReason}. " +
                "Remove it from this release before opening another.";
        }

        if (existing.Status == ReleasePullRequestStatus.Active)
        {
            return
                $"Prod → dev pull request #{existing.AzureDevOpsPullRequestId} is still active ({driftReason}). " +
                "Complete it before promoting.";
        }

        return null;
    }

    private async Task<CreatedPullRequestResult> CreateStoredPullRequestAsync(
        Guid userId,
        Guid releaseId,
        Guid teamId,
        RegisteredRepository repo,
        ReleasePrPhase phase,
        string sourceBranch,
        string targetBranch,
        string title,
        string? description,
        string? note,
        CancellationToken cancellationToken)
    {
        var created = await _ado.CreatePullRequestAsync(
            userId,
            organization: repo.AzureDevOpsOrganization,
            project: repo.AzureDevOpsProject,
            repositoryIdOrName: repo.RepositoryIdOrName,
            sourceRefName: sourceBranch,
            targetRefName: targetBranch,
            title: title,
            description: description,
            ct: cancellationToken);

        var id = Guid.NewGuid();
        _db.ReleasePullRequests.Add(new ReleasePullRequest
        {
            Id = id,
            ReleaseId = releaseId,
            TeamId = teamId,
            RegisteredRepositoryId = repo.Id,
            Phase = phase,
            Status = ReleasePullRequestStatus.Active,
            AzureDevOpsPullRequestId = created.PullRequestId,
            Url = created.Url,
            SourceRefName = sourceBranch,
            TargetRefName = targetBranch,
            Title = title,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _commitNotes.TryPopulateAsync(
            userId,
            releaseId,
            repo.Id,
            phase,
            sourceBranch,
            targetBranch,
            repo.AzureDevOpsOrganization,
            repo.AzureDevOpsProject,
            repo.RepositoryIdOrName,
            created.PullRequestId,
            cancellationToken);

        return new CreatedPullRequestResult(repo.RepositoryIdOrName, created.PullRequestId, created.Url, note, id);
    }

    private sealed record BranchDrift(bool ProdAhead, bool MasterAhead, string? Error);

    /// <summary>
    /// Master → prod requires a completed Dev → Master PR for the same release and repository.
    /// Refreshes the local status from Azure DevOps when a Dev→Master row exists but is not yet Completed.
    /// </summary>
    private async Task<string?> GetMasterToProdGateIssueAsync(
        Guid userId,
        Guid releaseId,
        RegisteredRepository repo,
        CancellationToken cancellationToken)
    {
        var devPr = await _db.ReleasePullRequests
            .FirstOrDefaultAsync(
                pr => pr.ReleaseId == releaseId
                      && pr.RegisteredRepositoryId == repo.Id
                      && pr.Phase == ReleasePrPhase.DevToMaster,
                cancellationToken);

        if (devPr is null)
        {
            return "Master → prod requires a completed Dev → Master pull request for this repository on the same release.";
        }

        if (devPr.Status == ReleasePullRequestStatus.Completed)
            return null;

        try
        {
            var status = await _ado.TryGetGitPullRequestStatusAsync(
                userId,
                repo.AzureDevOpsOrganization,
                repo.AzureDevOpsProject,
                repo.RepositoryIdOrName,
                devPr.AzureDevOpsPullRequestId,
                cancellationToken);

            devPr.Status = ReleasePullRequestStatusMapper.Map(status);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Fall through with last known status.
        }

        if (devPr.Status == ReleasePullRequestStatus.Completed)
            return null;

        return
            $"Dev → Master pull request #{devPr.AzureDevOpsPullRequestId} is '{devPr.Status}' — complete it before opening Master → Prod.";
    }

    /// <summary>
    /// Completes (merges, no fast-forward) every pull request stored for this release + phase.
    /// Continues past individual failures (already completed, conflicts, unmet policies, ...) and reports them per PR.
    /// </summary>
    public async Task<IReadOnlyList<CompletedPullRequestResult>> CompletePullRequestsForPhaseAsync(
        Guid releaseId,
        ReleasePrPhase phase,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();

        var release = await _db.Releases.AsNoTracking()
            .Where(r => r.Id == releaseId)
            .Select(r => new { r.Title, r.CreatedByUserId })
            .FirstOrDefaultAsync(cancellationToken);
        var releaseTitle = release?.Title ?? "Release";

        var prs = await _db.ReleasePullRequests
            .Where(pr => pr.ReleaseId == releaseId && pr.Phase == phase)
            .Include(pr => pr.RegisteredRepository)
            .Include(pr => pr.Team)
            .OrderBy(pr => pr.RegisteredRepository.RepositoryIdOrName)
            .ToListAsync(cancellationToken);

        var results = new List<CompletedPullRequestResult>();
        var completedIds = new List<Guid>();
        var failedLines = new List<WebhookPrLine>();
        foreach (var pr in prs)
        {
            var repo = pr.RegisteredRepository;
            try
            {
                var snapshot = await _ado.TryGetPullRequestSnapshotAsync(
                    userId,
                    repo.AzureDevOpsOrganization,
                    repo.AzureDevOpsProject,
                    repo.RepositoryIdOrName,
                    pr.AzureDevOpsPullRequestId,
                    cancellationToken);

                if (snapshot is null)
                {
                    const string missing = "Pull request no longer exists in Azure DevOps.";
                    results.Add(new CompletedPullRequestResult(
                        repo.RepositoryIdOrName, pr.AzureDevOpsPullRequestId, false, missing));
                    failedLines.Add(ToWebhookLine(pr, missing));
                    continue;
                }

                // Keep the locally stored status in sync with Azure DevOps regardless of outcome below.
                pr.Status = ReleasePullRequestStatusMapper.Map(snapshot.Status);

                // Already done or abandoned — not merge candidates. Skip quietly so Complete-all
                // does not inflate counts or emit batch_failure noise for non-active PRs.
                if (pr.Status is ReleasePullRequestStatus.Completed or ReleasePullRequestStatus.Abandoned)
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                if (string.IsNullOrEmpty(snapshot.LastMergeSourceCommitId))
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    const string noCommit = "Could not determine the merge source commit.";
                    results.Add(new CompletedPullRequestResult(
                        repo.RepositoryIdOrName, pr.AzureDevOpsPullRequestId, false, noCommit));
                    failedLines.Add(ToWebhookLine(pr, noCommit));
                    continue;
                }

                await _ado.CompletePullRequestAsync(
                    userId,
                    repo.AzureDevOpsOrganization,
                    repo.AzureDevOpsProject,
                    repo.RepositoryIdOrName,
                    pr.AzureDevOpsPullRequestId,
                    snapshot.LastMergeSourceCommitId,
                    cancellationToken);

                pr.Status = ReleasePullRequestStatus.Completed;
                await _db.SaveChangesAsync(cancellationToken);
                await NotifyPrMergedAsync(
                    MergeNotificationRecipient(pr.CreatedByUserId, release?.CreatedByUserId, fallback: userId),
                    releaseId, releaseTitle, pr, repo, cancellationToken);
                completedIds.Add(pr.Id);

                results.Add(new CompletedPullRequestResult(
                    repo.RepositoryIdOrName, pr.AzureDevOpsPullRequestId, true, null));
            }
            catch (HttpRequestException ex)
            {
                results.Add(new CompletedPullRequestResult(
                    repo.RepositoryIdOrName, pr.AzureDevOpsPullRequestId, false, ex.Message));
                failedLines.Add(ToWebhookLine(pr, ex.Message));
            }
        }

        await NotifyBatchFailuresAsync(releaseId, releaseTitle, phase, results, cancellationToken);
        if (completedIds.Count > 0 || failedLines.Count > 0)
            await _webhooks.NotifyPrMergedAsync(releaseId, phase, completedIds, failedLines, cancellationToken);
        await PromoteReleaseIfHasCompletedPullRequestAsync(releaseId, cancellationToken);
        return results;
    }

    /// <summary>
    /// Checks every stored pull request (optionally scoped to one phase) against Azure DevOps and updates
    /// the locally stored status. Does not attempt to complete/merge anything.
    /// </summary>
    public Task<IReadOnlyList<ReleasePullRequestStatusResult>> RefreshPullRequestStatusesAsync(
        Guid releaseId,
        ReleasePrPhase? phase,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        return RefreshPullRequestStatusesAsync(
            releaseId,
            phase,
            onlyActive: false,
            userFor: _ => userId,
            fallbackNotificationRecipient: userId,
            maxConcurrency: DefaultStatusLookupConcurrency,
            cancellationToken);
    }

    /// <summary>Default fan-out for per-PR status lookups within one release.</summary>
    public const int DefaultStatusLookupConcurrency = 4;

    /// <summary>
    /// Core of the status refresh. <paramref name="userFor"/> picks whose PAT checks each PR (the current
    /// user for interactive refreshes; the PR creator or any PAT holder for the background sync) and may
    /// return <c>null</c> to leave a PR at its last known status. With <paramref name="onlyActive"/>,
    /// completed and abandoned PRs are not re-checked, which keeps the scheduled sync cheap.
    /// Azure DevOps lookups run concurrently (bounded by <paramref name="maxConcurrency"/>); all DbContext
    /// work stays sequential.
    /// The PAT used for a lookup says nothing about who should hear about the merge: "PR merged" goes to
    /// the user who opened the PR, else the release creator, else <paramref name="fallbackNotificationRecipient"/>
    /// (the caller for interactive refreshes; null in the background sync, which then skips the in-app
    /// notification while chat webhooks still fire).
    /// </summary>
    public async Task<IReadOnlyList<ReleasePullRequestStatusResult>> RefreshPullRequestStatusesAsync(
        Guid releaseId,
        ReleasePrPhase? phase,
        bool onlyActive,
        Func<ReleasePullRequest, Guid?> userFor,
        Guid? fallbackNotificationRecipient,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        var release = await _db.Releases.AsNoTracking()
            .Where(r => r.Id == releaseId)
            .Select(r => new { r.Title, r.CreatedByUserId })
            .FirstOrDefaultAsync(cancellationToken);
        var releaseTitle = release?.Title ?? "Release";

        var query = _db.ReleasePullRequests.Where(pr => pr.ReleaseId == releaseId);
        if (phase is { } p)
            query = query.Where(pr => pr.Phase == p);

        var prs = await query.Include(pr => pr.RegisteredRepository).ToListAsync(cancellationToken);

        var toCheck = onlyActive
            ? prs.Where(pr => pr.Status == ReleasePullRequestStatus.Active).ToList()
            : prs;

        // Resolve PATs sequentially first: the resolver (and its DbContext) is not safe to call concurrently.
        var pats = new Dictionary<(Guid UserId, string Org), string>();
        var lookups = new List<(ReleasePullRequest Pr, Guid UserId, string Pat)>();
        InvalidOperationException? firstPatError = null;
        foreach (var pr in toCheck)
        {
            var userId = userFor(pr);
            if (userId is null)
                continue;

            var key = (userId.Value, pr.RegisteredRepository.AzureDevOpsOrganization.Trim().ToLowerInvariant());
            if (!pats.TryGetValue(key, out var pat))
            {
                try
                {
                    pat = await _patResolver.ResolvePatForOrganizationAsync(
                        userId.Value, pr.RegisteredRepository.AzureDevOpsOrganization, cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    // No usable PAT for this user/org: leave those PRs at their last known status.
                    firstPatError ??= ex;
                    continue;
                }
                pats[key] = pat;
            }
            lookups.Add((pr, userId.Value, pat));
        }

        // Partial refreshes (PAT for some orgs only) proceed quietly; if nothing at all could be checked
        // the caller gets the "add or renew your PAT" message instead of a silent no-op.
        if (lookups.Count == 0 && firstPatError is not null)
            throw firstPatError;

        using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var fetched = await Task.WhenAll(lookups.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var repo = item.Pr.RegisteredRepository;
                var status = await _ado.TryGetGitPullRequestStatusAsync(
                    item.Pat,
                    repo.AzureDevOpsOrganization,
                    repo.AzureDevOpsProject,
                    repo.RepositoryIdOrName,
                    item.Pr.AzureDevOpsPullRequestId,
                    cancellationToken);
                return (item.Pr, item.UserId, Status: (string?)status, Ok: true);
            }
            catch (HttpRequestException)
            {
                // Leave the last known status untouched; still report it so the caller sees something.
                return (item.Pr, item.UserId, Status: null, Ok: false);
            }
            finally
            {
                gate.Release();
            }
        }));

        var newlyCompleted = new List<ReleasePullRequest>();
        foreach (var (pr, _, status, ok) in fetched)
        {
            if (!ok)
                continue;

            var previous = pr.Status;
            pr.Status = ReleasePullRequestStatusMapper.Map(status);
            if (previous != ReleasePullRequestStatus.Completed &&
                pr.Status == ReleasePullRequestStatus.Completed)
                newlyCompleted.Add(pr);
        }

        // Persist every status first; notifications are best effort and must not cost a status update.
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var pr in newlyCompleted)
        {
            await NotifyPrMergedAsync(
                MergeNotificationRecipient(pr.CreatedByUserId, release?.CreatedByUserId, fallbackNotificationRecipient),
                releaseId, releaseTitle, pr, pr.RegisteredRepository, cancellationToken);
        }

        await NotifyPhaseCompleteWebhooksAsync(releaseId, prs, newlyCompleted, cancellationToken);
        await PromoteReleaseIfHasCompletedPullRequestAsync(releaseId, cancellationToken);
        return prs.Select(pr => new ReleasePullRequestStatusResult(pr.Id, pr.Status)).ToList();
    }

    private async Task NotifyPhaseCompleteWebhooksAsync(
        Guid releaseId,
        IReadOnlyList<ReleasePullRequest> scanned,
        IReadOnlyList<ReleasePullRequest> newlyCompleted,
        CancellationToken cancellationToken)
    {
        if (newlyCompleted.Count == 0)
            return;

        foreach (var phaseGroup in newlyCompleted.GroupBy(pr => pr.Phase))
        {
            var phasePrs = scanned.Where(pr => pr.Phase == phaseGroup.Key).ToList();
            if (phasePrs.Count == 0)
                continue;
            if (phasePrs.Any(pr => pr.Status != ReleasePullRequestStatus.Completed))
                continue;

            await _webhooks.NotifyPrMergedAsync(
                releaseId,
                phaseGroup.Key,
                phasePrs.Select(pr => pr.Id).ToList(),
                failed: [],
                cancellationToken);
        }
    }

    private static WebhookPrLine ToWebhookLine(ReleasePullRequest pr, string? failure)
    {
        var name = string.IsNullOrWhiteSpace(pr.RegisteredRepository.ServiceName)
            ? pr.RegisteredRepository.RepositoryIdOrName
            : pr.RegisteredRepository.ServiceName;
        return new WebhookPrLine(pr.Team.Name, name, pr.AzureDevOpsPullRequestId, pr.Url, failure);
    }

    /// <summary>
    /// Who gets the in-app "PR merged" notification: the user who opened the PR, else the release creator,
    /// else <paramref name="fallback"/>. Never the user whose PAT happened to be used for the status lookup.
    /// </summary>
    internal static Guid? MergeNotificationRecipient(Guid? pullRequestCreator, Guid? releaseCreator, Guid? fallback) =>
        pullRequestCreator ?? releaseCreator ?? fallback;

    /// <summary>Best effort: the merge itself is already persisted, so a failed notification is only logged.</summary>
    private async Task NotifyPrMergedAsync(
        Guid? recipient,
        Guid releaseId,
        string releaseTitle,
        ReleasePullRequest pr,
        RegisteredRepository repo,
        CancellationToken cancellationToken)
    {
        if (recipient is not { } userId)
            return;

        var label = string.IsNullOrWhiteSpace(repo.ServiceName) ? repo.RepositoryIdOrName : repo.ServiceName;
        try
        {
            await _notifications.NotifyOnceAsync(
                userId,
                kind: "pr_merged",
                dedupeKey: $"pr_merged:{pr.Id}",
                title: $"PR merged — {label}",
                body: $"#{pr.AzureDevOpsPullRequestId} on \"{releaseTitle}\" is completed in Azure DevOps.",
                href: $"/releases/{releaseId}",
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not record the merge notification for pull request {PullRequestId}.", pr.Id);
        }
    }

    private async Task NotifyBatchFailuresAsync(
        Guid releaseId,
        string releaseTitle,
        ReleasePrPhase phase,
        IReadOnlyList<CompletedPullRequestResult> results,
        CancellationToken cancellationToken)
    {
        var failed = results.Where(r => !r.Success).ToList();
        if (failed.Count == 0)
            return;

        var phaseLabel = phase switch
        {
            ReleasePrPhase.DevToMaster => "Dev → Master",
            ReleasePrPhase.MasterToProd => "Master → Prod",
            ReleasePrPhase.ProdToDev => "Prod → Dev",
            _ => phase.ToString()
        };

        var detail = string.Join(
            " · ",
            failed.Take(5).Select(f => $"{f.RepositoryIdOrName}: {f.Message ?? "failed"}"));
        if (failed.Count > 5)
            detail += $" · +{failed.Count - 5} more";

        await _notifications.NotifyOnceAsync(
            kind: "batch_failure",
            dedupeKey: $"batch_failure:{releaseId}:{phase}:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            title: $"Batch complete failed — {phaseLabel}",
            body: $"{failed.Count} of {results.Count} PRs failed on \"{releaseTitle}\". {detail}",
            href: $"/releases/{releaseId}",
            cancellationToken);
    }

    /// <summary>
    /// A release starts life as <see cref="ReleaseLifecycleStatus.Draft"/>. Opening the first PR batch
    /// (or confirming a completed PR) moves it to <see cref="ReleaseLifecycleStatus.Active"/>.
    /// </summary>
    private async Task PromoteReleaseToActiveIfDraftAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        var release = await _db.Releases.FirstOrDefaultAsync(r => r.Id == releaseId, cancellationToken);
        if (release is null || release.Status != ReleaseLifecycleStatus.Draft)
            return;

        release.Status = ReleaseLifecycleStatus.Active;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task PromoteReleaseIfHasCompletedPullRequestAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        var hasCompletedPullRequest = await _db.ReleasePullRequests.AnyAsync(
            pr => pr.ReleaseId == releaseId && pr.Status == ReleasePullRequestStatus.Completed,
            cancellationToken);

        if (!hasCompletedPullRequest)
            return;

        await PromoteReleaseToActiveIfDraftAsync(releaseId, cancellationToken);
    }

    /// <summary>
    /// Drops <see cref="ReleasePullRequest"/> rows when the linked Azure DevOps PR was abandoned or no longer exists,
    /// so a new batch can recreate PRs. Active or completed PRs still block.
    /// </summary>
    private async Task RemoveAbandonedOrMissingPullRequestRowsAsync(
        Guid userId,
        Guid releaseId,
        ReleasePrPhase phase,
        IReadOnlyList<Guid> repoIds,
        CancellationToken cancellationToken)
    {
        var conflicting = await _db.ReleasePullRequests
            .Where(pr =>
                pr.ReleaseId == releaseId &&
                pr.Phase == phase &&
                repoIds.Contains(pr.RegisteredRepositoryId))
            .Include(pr => pr.RegisteredRepository)
            .ToListAsync(cancellationToken);

        if (conflicting.Count == 0)
            return;

        var toRemove = new List<ReleasePullRequest>();
        foreach (var prRow in conflicting)
        {
            var repo = prRow.RegisteredRepository;
            string? status;
            try
            {
                status = await _ado.TryGetGitPullRequestStatusAsync(
                    userId,
                    repo.AzureDevOpsOrganization,
                    repo.AzureDevOpsProject,
                    repo.RepositoryIdOrName,
                    prRow.AzureDevOpsPullRequestId,
                    cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"Could not verify existing pull request #{prRow.AzureDevOpsPullRequestId} in Azure DevOps. " +
                    "Check your PAT and organization access. " + ex.Message);
            }

            if (ShouldRemoveStalePullRequestRow(status))
                toRemove.Add(prRow);
            else
            {
                throw new InvalidOperationException(
                    $"Pull request #{prRow.AzureDevOpsPullRequestId} for repository '{repo.RepositoryIdOrName}' is still " +
                    $"'{status ?? "unknown"}' in Azure DevOps for this release phase. " +
                    "Abandon it there (or wait until it completes) before creating another, or remove the entry from this release.");
            }
        }

        foreach (var row in toRemove)
            _db.ReleasePullRequests.Remove(row);

        if (toRemove.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>ADO returns status as a string (e.g. <c>abandoned</c>) or sometimes a numeric enum value.</summary>
    private static bool ShouldRemoveStalePullRequestRow(string? statusFromApi)
    {
        if (statusFromApi is null)
            return true;

        var s = statusFromApi.Trim();
        if (s.Length == 0)
            return false;

        if (int.TryParse(s, out var n))
        {
            // PullRequestStatus.Abandoned = 2 (Azure DevOps Git REST)
            return n == 2;
        }

        return s.Equals("abandoned", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureTeamEnrolledAsync(Guid releaseId, Guid teamId, CancellationToken cancellationToken)
    {
        var exists = await _db.ReleaseTeams.AnyAsync(
            rt => rt.ReleaseId == releaseId && rt.TeamId == teamId,
            cancellationToken);

        if (exists)
            return;

        _db.ReleaseTeams.Add(new ReleaseTeam
        {
            Id = Guid.NewGuid(),
            ReleaseId = releaseId,
            TeamId = teamId
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Clears branch overrides that match the other phase's defaults (common when optional from/to fields
    /// are kept after switching phase in the UI).
    /// </summary>
    private static BatchCreateReleasePullRequestsRequest SanitizeBranchOverrides(
        BatchCreateReleasePullRequestsRequest request)
    {
        if (request.Phase == ReleasePrPhase.MasterToProd &&
            (IsBranchPair(request.SourceBranch, request.TargetBranch, "dev", "master") ||
             IsBranchPair(request.SourceBranch, request.TargetBranch, "prod", "dev")))
            return request with { SourceBranch = null, TargetBranch = null };

        if (request.Phase == ReleasePrPhase.DevToMaster &&
            (IsBranchPair(request.SourceBranch, request.TargetBranch, "master", "prod") ||
             IsBranchPair(request.SourceBranch, request.TargetBranch, "prod", "dev")))
            return request with { SourceBranch = null, TargetBranch = null };

        if (request.Phase == ReleasePrPhase.ProdToDev &&
            (IsBranchPair(request.SourceBranch, request.TargetBranch, "dev", "master") ||
             IsBranchPair(request.SourceBranch, request.TargetBranch, "master", "prod")))
            return request with { SourceBranch = null, TargetBranch = null };

        return request;
    }

    private static bool IsBranchPair(string? source, string? target, string expectSource, string expectTarget)
    {
        var s = BranchShortName(source);
        var t = BranchShortName(target);
        if (s is null || t is null)
            return false;
        return s.Equals(expectSource, StringComparison.OrdinalIgnoreCase) &&
               t.Equals(expectTarget, StringComparison.OrdinalIgnoreCase);
    }

    private static string? BranchShortName(string? refOrBranch)
    {
        if (string.IsNullOrWhiteSpace(refOrBranch))
            return null;
        var b = refOrBranch.Trim();
        const string heads = "refs/heads/";
        if (b.StartsWith(heads, StringComparison.OrdinalIgnoreCase))
            b = b[heads.Length..];
        return b;
    }

    private static string BuildPullRequestTitle(ReleasePrPhase phase, string? sprintLabel)
    {
        var label = string.IsNullOrWhiteSpace(sprintLabel) ? "sprint ????/??" : sprintLabel.Trim();
        return phase switch
        {
            ReleasePrPhase.DevToMaster => $"Release dev into master - Release {label}",
            ReleasePrPhase.MasterToProd => $"Release master into prod - Release {label}",
            ReleasePrPhase.ProdToDev => $"Release prod into dev - Release {label}",
            _ => $"Release - Release {label}"
        };
    }

    private static string PromotionLabel(ReleasePrPhase phase) =>
        phase == ReleasePrPhase.MasterToProd ? "master → prod" : "dev → master";

    private static string BuildBackMergeDescription(string note, string? userDescription)
    {
        if (string.IsNullOrWhiteSpace(userDescription))
            return note;
        return note + "\n\n" + userDescription.Trim();
    }

    private static string ResolveSourceBranch(BatchCreateReleasePullRequestsRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceBranch))
            return request.SourceBranch.Trim();

        return request.Phase switch
        {
            ReleasePrPhase.DevToMaster => "dev",
            ReleasePrPhase.ProdToDev => "prod",
            _ => "master"
        };
    }

    private static string ResolveTargetBranch(BatchCreateReleasePullRequestsRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetBranch))
            return request.TargetBranch.Trim();

        return request.Phase switch
        {
            ReleasePrPhase.DevToMaster => "master",
            ReleasePrPhase.ProdToDev => "dev",
            _ => "prod"
        };
    }
}
