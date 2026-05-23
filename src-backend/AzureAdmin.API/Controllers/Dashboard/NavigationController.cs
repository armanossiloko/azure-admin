using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Services.AzureDevOps;
using AzureAdmin.API.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Controllers.Dashboard;

[ApiController]
[Authorize]
[Route("api/navigation")]
public sealed class NavigationController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly AzureDevOpsOrganizationService _organizations;
    private readonly ICurrentUser _currentUser;

    public NavigationController(
        ApplicationDbContext db,
        AzureDevOpsOrganizationService organizations,
        ICurrentUser currentUser)
    {
        _db = db;
        _organizations = organizations;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<NavigationSummaryDto>> Get(CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var since = DateTimeOffset.UtcNow.AddHours(-48);

        var orgSummaries = await _organizations.ListAsync(cancellationToken);
        var organizations = orgSummaries
            .Select(o => new NavigationOrganizationDto(o.Id, o.OrganizationDisplay, o.OrganizationKey, o.HasPatCredential))
            .ToList();

        var activityPreview = await BuildActivityPreviewAsync(since, userId, cancellationToken);

        // Reserved for future in-app notifications; always zero until a notifications store exists.
        const int unreadNotificationsCount = 0;

        return Ok(new NavigationSummaryDto(organizations, activityPreview, unreadNotificationsCount));
    }

    private async Task<IReadOnlyList<DashboardActivityDto>> BuildActivityPreviewAsync(
        DateTimeOffset since,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var releaseRows = await _db.Releases.AsNoTracking()
            .Where(r => r.CreatedAt >= since)
            .OrderByDescending(r => r.CreatedAt)
            .Take(4)
            .Select(r => new DashboardActivityDto(
                "release",
                r.Title,
                r.SprintLabel != null ? $"Sprint {r.SprintLabel}" : null,
                r.CreatedAt,
                $"/releases/{r.Id}"))
            .ToListAsync(cancellationToken);

        var prRows = await _db.ReleasePullRequests.AsNoTracking()
            .Where(pr => pr.CreatedAt >= since)
            .OrderByDescending(pr => pr.CreatedAt)
            .Take(4)
            .Select(pr => new DashboardActivityDto(
                "pull_request",
                pr.Title,
                pr.Release.Title,
                pr.CreatedAt,
                $"/releases/{pr.ReleaseId}"))
            .ToListAsync(cancellationToken);

        return releaseRows
            .Concat(prRows)
            .OrderByDescending(a => a.OccurredAt)
            .Take(6)
            .ToList();
    }
}
