using AzureAdmin.Api.Contracts;
using AzureAdmin.Api.Data;
using AzureAdmin.Api.Models;
using AzureAdmin.Api.Services.AzureDevOps;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.Api.Services.Releases;

public sealed class ReleaseCommitNotesService
{
    private const int MaxCommitsPerSource = 200;

    private readonly ApplicationDbContext _db;
    private readonly AzureDevOpsClient _ado;
    private readonly ILogger<ReleaseCommitNotesService> _logger;

    public ReleaseCommitNotesService(
        ApplicationDbContext db,
        AzureDevOpsClient ado,
        ILogger<ReleaseCommitNotesService> logger)
    {
        _db = db;
        _ado = ado;
        _logger = logger;
    }

    public async Task TryPopulateAsync(
        Guid userId,
        Guid releaseId,
        Guid registeredRepositoryId,
        ReleasePrPhase phase,
        string sourceRefName,
        string targetRefName,
        string azureDevOpsOrganization,
        string azureDevOpsProject,
        string repositoryIdOrName,
        int? azureDevOpsPullRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await LoadCommitsAsync(
                userId,
                azureDevOpsOrganization,
                azureDevOpsProject,
                repositoryIdOrName,
                sourceRefName,
                targetRefName,
                azureDevOpsPullRequestId,
                pullRequestIds: null,
                cancellationToken);

            await PersistAsync(
                releaseId,
                registeredRepositoryId,
                phase,
                sourceRefName,
                targetRefName,
                rows,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not load Git commits for release {ReleaseId}, repository {Repo}, phase {Phase}.",
                releaseId,
                repositoryIdOrName,
                phase);
        }
    }

    /// <summary>Rebuilds notes from all PRs on the release (useful after manually fixing data).</summary>
    public async Task RefreshNotesForReleaseAsync(Guid userId, Guid releaseId, CancellationToken cancellationToken)
    {
        var prs = await _db.ReleasePullRequests.AsNoTracking()
            .Where(p => p.ReleaseId == releaseId)
            .Include(p => p.RegisteredRepository)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        var groups = prs.GroupBy(p => (p.RegisteredRepositoryId, p.Phase));
        foreach (var group in groups)
        {
            var latest = group.First();
            var r = latest.RegisteredRepository;
            var prIds = group.Select(p => p.AzureDevOpsPullRequestId).Distinct().ToList();

            try
            {
                var rows = await LoadCommitsAsync(
                    userId,
                    r.AzureDevOpsOrganization,
                    r.AzureDevOpsProject,
                    r.RepositoryIdOrName,
                    latest.SourceRefName,
                    latest.TargetRefName,
                    azureDevOpsPullRequestId: null,
                    pullRequestIds: prIds,
                    cancellationToken);

                await PersistAsync(
                    releaseId,
                    latest.RegisteredRepositoryId,
                    latest.Phase,
                    latest.SourceRefName,
                    latest.TargetRefName,
                    rows,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not refresh commit notes for release {ReleaseId}, repository {Repo}, phase {Phase}.",
                    releaseId,
                    r.RepositoryIdOrName,
                    latest.Phase);
            }
        }
    }

    private async Task<IReadOnlyList<ReleaseCommitItemDto>> LoadCommitsAsync(
        Guid userId,
        string organization,
        string project,
        string repositoryIdOrName,
        string sourceRefName,
        string targetRefName,
        int? azureDevOpsPullRequestId,
        IReadOnlyList<int>? pullRequestIds,
        CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, ReleaseCommitItemDto>(StringComparer.OrdinalIgnoreCase);

        if (pullRequestIds is not null)
        {
            foreach (var prId in pullRequestIds)
                await MergePullRequestCommitsAsync(
                    merged, userId, organization, project, repositoryIdOrName, prId, cancellationToken);
        }
        else if (azureDevOpsPullRequestId is int singlePrId)
        {
            await MergePullRequestCommitsAsync(
                merged, userId, organization, project, repositoryIdOrName, singlePrId, cancellationToken);
        }

        if (merged.Count > 0)
            return SortCommits(merged.Values);

        var branchRows = await _ado.GetCommitsBetweenBranchesAsync(
            userId,
            organization,
            project,
            repositoryIdOrName,
            RefBranchName(sourceRefName),
            RefBranchName(targetRefName),
            MaxCommitsPerSource,
            cancellationToken);

        foreach (var row in branchRows)
            merged[row.CommitId] = ToDto(row);

        return SortCommits(merged.Values);
    }

    private async Task MergePullRequestCommitsAsync(
        Dictionary<string, ReleaseCommitItemDto> merged,
        Guid userId,
        string organization,
        string project,
        string repositoryIdOrName,
        int pullRequestId,
        CancellationToken cancellationToken)
    {
        var rows = await _ado.GetPullRequestCommitsAsync(
            userId,
            organization,
            project,
            repositoryIdOrName,
            pullRequestId,
            MaxCommitsPerSource,
            cancellationToken);

        foreach (var row in rows)
            merged[row.CommitId] = ToDto(row);
    }

    private async Task PersistAsync(
        Guid releaseId,
        Guid registeredRepositoryId,
        ReleasePrPhase phase,
        string sourceRefName,
        string targetRefName,
        IReadOnlyList<ReleaseCommitItemDto> items,
        CancellationToken cancellationToken)
    {
        var json = ReleaseCommitJson.Serialize(items);

        var entity = await _db.ReleaseRepositoryCommitNotes.FirstOrDefaultAsync(
            n => n.ReleaseId == releaseId &&
                 n.RegisteredRepositoryId == registeredRepositoryId &&
                 n.Phase == phase,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (entity is null)
        {
            _db.ReleaseRepositoryCommitNotes.Add(new ReleaseRepositoryCommitNotes
            {
                Id = Guid.NewGuid(),
                ReleaseId = releaseId,
                RegisteredRepositoryId = registeredRepositoryId,
                Phase = phase,
                SourceRefName = sourceRefName,
                TargetRefName = targetRefName,
                CommitsJson = json,
                FetchedAt = now
            });
        }
        else
        {
            entity.SourceRefName = sourceRefName;
            entity.TargetRefName = targetRefName;
            entity.CommitsJson = json;
            entity.FetchedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stored {Count} commit note(s) for release {ReleaseId}, repository {RepoId}, phase {Phase}.",
            items.Count,
            releaseId,
            registeredRepositoryId,
            phase);
    }

    private static ReleaseCommitItemDto ToDto(AzureDevOpsCommitBrief row) =>
        new(row.CommitId, row.Comment, row.AuthorName, row.CommittedDate);

    private static List<ReleaseCommitItemDto> SortCommits(IEnumerable<ReleaseCommitItemDto> items) =>
        items
            .OrderByDescending(c => c.CommittedDate)
            .ThenBy(c => c.CommitId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string RefBranchName(string refName)
    {
        refName = refName.Trim();
        const string p = "refs/heads/";
        if (refName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            return refName[p.Length..];
        return refName;
    }
}
