using AzureAdmin.API.Contracts.Search;
using AzureAdmin.API.Data;
using AzureAdmin.API.Services.AzureDevOps;
using AzureAdmin.API.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Controllers.Search;

[ApiController]
[Authorize]
[Route("api/search")]
public sealed class SearchController : ControllerBase
{
    private const int MaxHits = 20;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AzureDevOpsOrganizationService _organizations;
    private readonly OrganizationScopeService _orgScope;

    public SearchController(
        ApplicationDbContext db,
        ICurrentUser currentUser,
        AzureDevOpsOrganizationService organizations,
        OrganizationScopeService orgScope)
    {
        _db = db;
        _currentUser = currentUser;
        _organizations = organizations;
        _orgScope = orgScope;
    }

    [HttpGet]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] string? q,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var term = (q ?? "").Trim();
        if (term.Length < 2)
            return Ok(new SearchResultDto([]));

        var pattern = $"%{term}%";
        var userId = _currentUser.GetRequiredUserId();
        var hits = new List<SearchHitDto>();

        OrganizationScope? scope = null;
        HashSet<Guid>? matchingReleaseIds = null;
        HashSet<Guid>? matchingTeamIds = null;
        if (organizationId is { } orgId)
        {
            try
            {
                scope = await _orgScope.ResolveAsync(orgId, userId, cancellationToken);
                if (scope is not null)
                {
                    matchingReleaseIds = await _orgScope.GetMatchingReleaseIdsAsync(scope, cancellationToken);
                    matchingTeamIds = await _orgScope.GetMatchingTeamIdsAsync(scope, cancellationToken);
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        var releaseQuery = _db.Releases.AsNoTracking()
            .Where(r => EF.Functions.ILike(r.Title, pattern) ||
                        (r.SprintLabel != null && EF.Functions.ILike(r.SprintLabel, pattern)));
        if (matchingReleaseIds is not null)
            releaseQuery = releaseQuery.Where(r => matchingReleaseIds.Contains(r.Id));

        var releases = await releaseQuery
            .OrderByDescending(r => r.CreatedAt)
            .Take(6)
            .Select(r => new SearchHitDto(
                "release",
                r.Title,
                r.SprintLabel != null ? $"Sprint {r.SprintLabel}" : null,
                $"/releases/{r.Id}"))
            .ToListAsync(cancellationToken);
        hits.AddRange(releases);

        var teamQuery = _db.Teams.AsNoTracking()
            .Where(t => EF.Functions.ILike(t.Name, pattern));
        if (matchingTeamIds is not null)
            teamQuery = teamQuery.Where(t => matchingTeamIds.Contains(t.Id));

        var teams = await teamQuery
            .OrderBy(t => t.Name)
            .Take(5)
            .Select(t => new SearchHitDto("team", t.Name, null, "/teams"))
            .ToListAsync(cancellationToken);
        hits.AddRange(teams);

        var repoQuery = _db.RegisteredRepositories.AsNoTracking()
            .Where(r => EF.Functions.ILike(r.RepositoryIdOrName, pattern) ||
                        EF.Functions.ILike(r.AzureDevOpsProject, pattern) ||
                        EF.Functions.ILike(r.AzureDevOpsOrganization, pattern) ||
                        (r.ServiceName != null && EF.Functions.ILike(r.ServiceName, pattern)));
        if (scope is not null)
            repoQuery = _orgScope.FilterRepositories(repoQuery, scope);

        var repos = await repoQuery
            .OrderBy(r => r.AzureDevOpsOrganization)
            .ThenBy(r => r.RepositoryIdOrName)
            .Take(6)
            .Select(r => new SearchHitDto(
                "repository",
                r.ServiceName ?? r.RepositoryIdOrName,
                r.IsDecommissioned
                    ? $"{r.AzureDevOpsProject} · {r.AzureDevOpsOrganization} · Decommissioned"
                    : $"{r.AzureDevOpsProject} · {r.AzureDevOpsOrganization}",
                "/repositories"))
            .ToListAsync(cancellationToken);
        hits.AddRange(repos);

        var orgSummaries = await _organizations.ListAsync(cancellationToken);
        var orgHits = orgSummaries
            .Where(o => o.OrganizationDisplay.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        o.OrganizationKey.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .Select(o => new SearchHitDto(
                "organization",
                o.OrganizationDisplay,
                o.HasPatCredential ? "PAT stored" : "No PAT",
                $"/settings/azure-organizations/{o.Id}"))
            .ToList();
        hits.AddRange(orgHits);

        return Ok(new SearchResultDto(hits.Take(MaxHits).ToList()));
    }
}
