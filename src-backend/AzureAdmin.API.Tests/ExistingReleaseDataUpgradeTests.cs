using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Releases;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Tests;

/// <summary>
/// Rows that existed before this PR: no creator, no last-edit time, stamp filled in by the metadata
/// migration. They must keep working without a re-save.
/// </summary>
public sealed class ExistingReleaseDataUpgradeTests
{
    private static DbContextOptions<ApplicationDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static async Task<(Guid ReleaseId, Guid PullRequestId)> SeedLegacyReleaseAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        var team = new Team { Id = Guid.NewGuid(), Name = "Platform" };
        var repo = new RegisteredRepository
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            AzureDevOpsOrganization = "contoso",
            AzureDevOpsProject = "proj",
            RepositoryIdOrName = "api"
        };
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Title = "Sprint 2026/40",
            Status = ReleaseLifecycleStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            CreatedByUserId = null,
            UpdatedAt = null,
            ConcurrencyStamp = Guid.NewGuid()
        };
        var pr = new ReleasePullRequest
        {
            Id = Guid.NewGuid(),
            ReleaseId = release.Id,
            Release = release,
            TeamId = team.Id,
            Team = team,
            RegisteredRepositoryId = repo.Id,
            RegisteredRepository = repo,
            Phase = ReleasePrPhase.DevToMaster,
            Status = ReleasePullRequestStatus.Active,
            AzureDevOpsPullRequestId = 42,
            Url = "https://dev.azure.com/contoso/proj/_git/api/pullrequest/42",
            SourceRefName = "refs/heads/dev",
            TargetRefName = "refs/heads/master",
            Title = "dev → master",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            CreatedByUserId = null
        };
        db.ReleasePullRequests.Add(pr);
        await db.SaveChangesAsync();
        return (release.Id, pr.Id);
    }

    [Fact]
    public async Task Editing_a_legacy_release_does_not_invent_a_creator()
    {
        var options = NewDatabase();
        var (releaseId, _) = await SeedLegacyReleaseAsync(options);

        await using var db = new ApplicationDbContext(options);
        var release = await db.Releases.SingleAsync(r => r.Id == releaseId);
        var stampBefore = release.ConcurrencyStamp;

        release.Title = "Sprint 2026/40 (renamed)";
        await db.SaveChangesAsync();

        Assert.Null(release.CreatedByUserId);
        Assert.NotNull(release.UpdatedAt);
        Assert.NotEqual(stampBefore, release.ConcurrencyStamp);
    }

    [Fact]
    public async Task A_legacy_pull_request_keeps_its_creator_empty_when_status_changes()
    {
        var options = NewDatabase();
        var (_, prId) = await SeedLegacyReleaseAsync(options);

        await using var db = new ApplicationDbContext(options);
        var pr = await db.ReleasePullRequests.SingleAsync(r => r.Id == prId);
        pr.Status = ReleasePullRequestStatus.Completed;
        await db.SaveChangesAsync();

        Assert.Null(pr.CreatedByUserId);
        Assert.Equal(ReleasePullRequestStatus.Completed, pr.Status);
    }

    [Fact]
    public void Merge_notifications_for_legacy_rows_do_not_require_a_creator()
    {
        var releaseCreator = Guid.NewGuid();

        Assert.Equal(
            releaseCreator,
            ReleasePullRequestBatchService.MergeNotificationRecipient(null, releaseCreator, fallback: null));
        Assert.Null(ReleasePullRequestBatchService.MergeNotificationRecipient(null, null, fallback: null));
    }

    [Fact]
    public async Task A_pat_saved_without_expiry_is_still_a_sync_holder()
    {
        var options = NewDatabase();
        var userId = Guid.NewGuid();
        await using (var db = new ApplicationDbContext(options))
        {
            db.AzureDevOpsPatCredentials.Add(new AzureDevOpsPatCredential
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OrganizationKey = "contoso",
                OrganizationDisplay = "Contoso",
                ProtectedPat = [1, 2, 3],
                PatExpiresAt = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using var read = new ApplicationDbContext(options);
        var now = DateTimeOffset.UtcNow;
        // Same filter as PullRequestStatusSyncService: null expiry means "still valid".
        var holders = await read.AzureDevOpsPatCredentials.AsNoTracking()
            .Where(c => c.PatExpiresAt == null || c.PatExpiresAt > now)
            .Select(c => new PatHolder(c.UserId, c.OrganizationKey, c.UpdatedAt))
            .ToListAsync();

        Assert.Contains(holders, h => h.UserId == userId && h.OrganizationKey == "contoso");
    }
}
