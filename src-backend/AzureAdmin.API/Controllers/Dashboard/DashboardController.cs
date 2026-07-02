using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Controllers.Dashboard;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DashboardController(ApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get([FromQuery] int recentHours = 48, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();
        var since = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(recentHours, 1, 168));

        var activeReleaseStatuses = new[] { ReleaseLifecycleStatus.Draft, ReleaseLifecycleStatus.Active };

        var activeReleasesCount = await _db.Releases.AsNoTracking()
            .CountAsync(r => activeReleaseStatuses.Contains(r.Status), cancellationToken);

        var openPullRequestsCount = await _db.ReleasePullRequests.AsNoTracking()
            .CountAsync(pr =>
                activeReleaseStatuses.Contains(pr.Release.Status) &&
                pr.Status != ReleasePullRequestStatus.Completed &&
                pr.Status != ReleasePullRequestStatus.Abandoned,
                cancellationToken);

        var pullRequestsNeedingAttentionCount = await _db.ReleasePullRequests.AsNoTracking()
            .CountAsync(pr =>
                pr.Release.Status == ReleaseLifecycleStatus.Draft &&
                pr.Status != ReleasePullRequestStatus.Completed &&
                pr.Status != ReleasePullRequestStatus.Abandoned,
                cancellationToken);

        var registeredRepositoriesCount = await _db.RegisteredRepositories.AsNoTracking()
            .CountAsync(cancellationToken);

        var distinctAzureDevOpsProjectsCount = await _db.RegisteredRepositories.AsNoTracking()
            .Select(r => new { r.AzureDevOpsOrganization, r.AzureDevOpsProject })
            .Distinct()
            .CountAsync(cancellationToken);

        var hasAzureOrganization = await _db.UserAzureDevOpsOrganizations.AsNoTracking()
            .AnyAsync(o => o.UserId == userId, cancellationToken);

        var hasTeam = await _db.Teams.AsNoTracking().AnyAsync(cancellationToken);
        var hasRegisteredRepository = registeredRepositoriesCount > 0;
        var hasRelease = await _db.Releases.AsNoTracking().AnyAsync(cancellationToken);

        var checklist = new DashboardChecklistDto(
            hasAzureOrganization,
            hasTeam,
            hasRegisteredRepository,
            hasRelease);

        var activeReleaseHighlightRows = await _db.Releases.AsNoTracking()
            .Where(r => activeReleaseStatuses.Contains(r.Status))
            .OrderByDescending(r => r.CreatedAt)
            .Take(5)
            .Select(r => new { r.Id, r.Title, r.SprintLabel, r.Status, r.CreatedAt })
            .ToListAsync(cancellationToken);

        var activeReleaseHighlights = activeReleaseHighlightRows
            .Select(r => new DashboardReleaseSummaryDto(
                r.Id,
                r.Title,
                r.SprintLabel,
                FormatLifecycleStatus(r.Status),
                r.CreatedAt))
            .ToList();

        var stats = new DashboardStatsDto(
            activeReleasesCount,
            openPullRequestsCount,
            pullRequestsNeedingAttentionCount,
            registeredRepositoriesCount,
            distinctAzureDevOpsProjectsCount);

        var recentActivity = await BuildRecentActivityAsync(since, userId, cancellationToken);

        return Ok(new DashboardDto(stats, checklist, activeReleaseHighlights, recentActivity));
    }

    private async Task<IReadOnlyList<DashboardActivityDto>> BuildRecentActivityAsync(
        DateTimeOffset since,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var releaseRows = await _db.Releases.AsNoTracking()
            .Where(r => r.CreatedAt >= since)
            .OrderByDescending(r => r.CreatedAt)
            .Take(12)
            .Select(r => new DashboardActivityDto(
                "release",
                $"Release: {r.Title}",
                r.SprintLabel != null ? $"Sprint {r.SprintLabel}" : null,
                r.CreatedAt,
                $"/releases/{r.Id}"))
            .ToListAsync(cancellationToken);

        var prRows = await _db.ReleasePullRequests.AsNoTracking()
            .Where(pr => pr.CreatedAt >= since)
            .OrderByDescending(pr => pr.CreatedAt)
            .Take(12)
            .Select(pr => new DashboardActivityDto(
                "pull_request",
                pr.Title,
                pr.Release.Title,
                pr.CreatedAt,
                $"/releases/{pr.ReleaseId}"))
            .ToListAsync(cancellationToken);

        var orgRows = await _db.UserAzureDevOpsOrganizations.AsNoTracking()
            .Where(o => o.UserId == userId && o.CreatedAt >= since)
            .OrderByDescending(o => o.CreatedAt)
            .Take(8)
            .Select(o => new DashboardActivityDto(
                "organization",
                $"Connected organization {o.OrganizationDisplay}",
                null,
                o.CreatedAt,
                $"/settings/azure-organizations/{o.Id}"))
            .ToListAsync(cancellationToken);

        return releaseRows
            .Concat(prRows)
            .Concat(orgRows)
            .OrderByDescending(a => a.OccurredAt)
            .Take(20)
            .ToList();
    }

    private static string FormatLifecycleStatus(ReleaseLifecycleStatus status) =>
        status switch
        {
            ReleaseLifecycleStatus.Draft => "Draft",
            ReleaseLifecycleStatus.Active => "Active",
            ReleaseLifecycleStatus.Completed => "Completed",
            ReleaseLifecycleStatus.Archived => "Archived",
            _ => status.ToString()
        };
}
