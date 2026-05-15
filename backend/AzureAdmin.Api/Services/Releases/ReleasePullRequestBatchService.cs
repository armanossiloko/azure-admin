using System.Net.Http;
using AzureAdmin.Api.Contracts;
using AzureAdmin.Api.Data;
using AzureAdmin.Api.Models;
using AzureAdmin.Api.Services.AzureDevOps;
using AzureAdmin.Api.Services.Identity;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.Api.Services.Releases;

public sealed class ReleasePullRequestBatchService
{
    private readonly ApplicationDbContext _db;
    private readonly AzureDevOpsClient _ado;
    private readonly ICurrentUser _currentUser;
    private readonly ReleaseCommitNotesService _commitNotes;

    public ReleasePullRequestBatchService(
        ApplicationDbContext db,
        AzureDevOpsClient ado,
        ICurrentUser currentUser,
        ReleaseCommitNotesService commitNotes)
    {
        _db = db;
        _ado = ado;
        _currentUser = currentUser;
        _commitNotes = commitNotes;
    }

    public async Task<IReadOnlyList<CreatedPullRequestResult>> CreatePullRequestsForReleaseAsync(
        Guid releaseId,
        Guid teamId,
        BatchCreateReleasePullRequestsRequest request,
        CancellationToken cancellationToken)
    {
        var release = await _db.Releases.AsNoTracking()
            .Where(r => r.Id == releaseId)
            .Select(r => new { r.SprintLabel })
            .FirstOrDefaultAsync(cancellationToken);
        if (release is null)
            throw new InvalidOperationException("Release was not found.");

        var teamExists = await _db.Teams.AnyAsync(t => t.Id == teamId, cancellationToken);
        if (!teamExists)
            throw new InvalidOperationException("Team was not found.");

        await EnsureTeamEnrolledAsync(releaseId, teamId, cancellationToken);

        request = SanitizeBranchOverrides(request);

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

        var userId = _currentUser.GetRequiredUserId();
        await RemoveAbandonedOrMissingPullRequestRowsAsync(
            userId,
            releaseId,
            request.Phase,
            repoIds,
            cancellationToken);

        var sourceBranch = ResolveSourceBranch(request);
        var targetBranch = ResolveTargetBranch(request);
        var pullRequestTitle = BuildPullRequestTitle(request.Phase, release.SprintLabel);

        var results = new List<CreatedPullRequestResult>();
        foreach (var repo in repos)
        {
            var created = await _ado.CreatePullRequestAsync(
                userId,
                organization: repo.AzureDevOpsOrganization,
                project: repo.AzureDevOpsProject,
                repositoryIdOrName: repo.RepositoryIdOrName,
                sourceRefName: sourceBranch,
                targetRefName: targetBranch,
                title: pullRequestTitle,
                description: request.Description,
                ct: cancellationToken);

            var entity = new ReleasePullRequest
            {
                Id = Guid.NewGuid(),
                ReleaseId = releaseId,
                TeamId = teamId,
                RegisteredRepositoryId = repo.Id,
                Phase = request.Phase,
                AzureDevOpsPullRequestId = created.PullRequestId,
                Url = created.Url,
                SourceRefName = sourceBranch,
                TargetRefName = targetBranch,
                Title = pullRequestTitle,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _db.ReleasePullRequests.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);
            results.Add(new CreatedPullRequestResult(repo.RepositoryIdOrName, created.PullRequestId, created.Url));

            await _commitNotes.TryPopulateAsync(
                userId,
                releaseId,
                repo.Id,
                request.Phase,
                sourceBranch,
                targetBranch,
                repo.AzureDevOpsOrganization,
                repo.AzureDevOpsProject,
                repo.RepositoryIdOrName,
                created.PullRequestId,
                cancellationToken);
        }

        return results;
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
            IsBranchPair(request.SourceBranch, request.TargetBranch, "dev", "master"))
            return request with { SourceBranch = null, TargetBranch = null };

        if (request.Phase == ReleasePrPhase.DevToMaster &&
            IsBranchPair(request.SourceBranch, request.TargetBranch, "master", "prod"))
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
        return phase == ReleasePrPhase.DevToMaster
            ? $"Release dev into master - Release {label}"
            : $"Release master into prod - Release {label}";
    }

    private static string ResolveSourceBranch(BatchCreateReleasePullRequestsRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceBranch))
            return request.SourceBranch.Trim();

        return request.Phase == ReleasePrPhase.DevToMaster ? "dev" : "master";
    }

    private static string ResolveTargetBranch(BatchCreateReleasePullRequestsRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetBranch))
            return request.TargetBranch.Trim();

        return request.Phase == ReleasePrPhase.DevToMaster ? "master" : "prod";
    }
}
