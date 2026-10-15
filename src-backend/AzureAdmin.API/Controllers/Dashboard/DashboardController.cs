using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.AzureDevOps;
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
    private readonly OrganizationScopeService _orgScope;

    public DashboardController(
        ApplicationDbContext db,
        ICurrentUser currentUser,
        OrganizationScopeService orgScope)
    {
        _db = db;
        _currentUser = currentUser;
        _orgScope = orgScope;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get(
        [FromQuery] int recentHours = 48,
        [FromQuery] Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.GetRequiredUserId();
        var since = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(recentHours, 1, 168));

        OrganizationScope? scope = null;
        HashSet<Guid>? matchingReleaseIds = null;
        try
        {
            scope = await _orgScope.ResolveAsync(organizationId, userId, cancellationToken);
            if (scope is not null)
                matchingReleaseIds = await _orgScope.GetMatchingReleaseIdsAsync(scope, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var activeReleaseStatuses = new[] { ReleaseLifecycleStatus.Draft, ReleaseLifecycleStatus.Active };

        var releasesQuery = _db.Releases.AsNoTracking().AsQueryable();
        if (matchingReleaseIds is not null)
            releasesQuery = releasesQuery.Where(r => matchingReleaseIds.Contains(r.Id));

        var activeReleasesCount = await releasesQuery
            .CountAsync(r => activeReleaseStatuses.Contains(r.Status), cancellationToken);

        var prQuery = _db.ReleasePullRequests.AsNoTracking().AsQueryable();
        if (matchingReleaseIds is not null)
            prQuery = prQuery.Where(pr => matchingReleaseIds.Contains(pr.ReleaseId));
        if (scope is not null)
        {
            prQuery = prQuery.Where(pr =>
                pr.RegisteredRepository.AzureDevOpsOrganization == scope.Display ||
                pr.RegisteredRepository.AzureDevOpsOrganization.ToLower() == scope.Key);
        }

        var openPullRequestsCount = await prQuery
            .CountAsync(pr =>
                activeReleaseStatuses.Contains(pr.Release.Status) &&
                pr.Status != ReleasePullRequestStatus.Completed &&
                pr.Status != ReleasePullRequestStatus.Abandoned,
                cancellationToken);

        // Draft + Active: open PRs still need attention after promotion out of Draft.
        var pullRequestsNeedingAttentionCount = await prQuery
            .CountAsync(pr =>
                activeReleaseStatuses.Contains(pr.Release.Status) &&
                pr.Status != ReleasePullRequestStatus.Completed &&
                pr.Status != ReleasePullRequestStatus.Abandoned,
                cancellationToken);

        var reposQuery = _db.RegisteredRepositories.AsNoTracking().AsQueryable();
        if (scope is not null)
            reposQuery = _orgScope.FilterRepositories(reposQuery, scope);

        var registeredRepositoriesCount = await reposQuery.CountAsync(cancellationToken);

        var distinctAzureDevOpsProjectsCount = await reposQuery
            .Select(r => new { r.AzureDevOpsOrganization, r.AzureDevOpsProject })
            .Distinct()
            .CountAsync(cancellationToken);

        var hasAzureOrganization = await _db.UserAzureDevOpsOrganizations.AsNoTracking()
            .AnyAsync(o => o.UserId == userId, cancellationToken);

        var hasTeam = await _db.Teams.AsNoTracking().AnyAsync(cancellationToken);
        var hasRegisteredRepository = registeredRepositoriesCount > 0;
        var hasRelease = await releasesQuery.AnyAsync(cancellationToken);

        var checklist = new DashboardChecklistDto(
            hasAzureOrganization,
            hasTeam,
            hasRegisteredRepository,
            hasRelease);

        var activeReleaseHighlightRows = await releasesQuery
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

        var recentActivity = await BuildRecentActivityAsync(since, userId, matchingReleaseIds, scope, cancellationToken);

        return Ok(new DashboardDto(stats, checklist, activeReleaseHighlights, recentActivity));
    }

    private async Task<IReadOnlyList<DashboardActivityDto>> BuildRecentActivityAsync(
        DateTimeOffset since,
        Guid userId,
        HashSet<Guid>? matchingReleaseIds,
        OrganizationScope? scope,
        CancellationToken cancellationToken)
    {
        var releaseQuery = _db.Releases.AsNoTracking().Where(r => r.CreatedAt >= since);
        if (matchingReleaseIds is not null)
            releaseQuery = releaseQuery.Where(r => matchingReleaseIds.Contains(r.Id));

        var releaseRows = await releaseQuery
            .OrderByDescending(r => r.CreatedAt)
            .Take(12)
            .Select(r => new DashboardActivityDto(
                "release",
                $"Release: {r.Title}",
                r.SprintLabel != null ? $"Sprint {r.SprintLabel}" : null,
                r.CreatedAt,
                $"/releases/{r.Id}"))
            .ToListAsync(cancellationToken);

        var prQuery = _db.ReleasePullRequests.AsNoTracking().Where(pr => pr.CreatedAt >= since);
        if (matchingReleaseIds is not null)
            prQuery = prQuery.Where(pr => matchingReleaseIds.Contains(pr.ReleaseId));
        if (scope is not null)
        {
            prQuery = prQuery.Where(pr =>
                pr.RegisteredRepository.AzureDevOpsOrganization == scope.Display ||
                pr.RegisteredRepository.AzureDevOpsOrganization.ToLower() == scope.Key);
        }

        var prRows = await prQuery
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
