using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Services.AzureDevOps;

public sealed record OrganizationScope(string Display, string Key);

/// <summary>
/// Resolves the selected user ADO org and matches registered repositories by display name or key slug.
/// </summary>
public sealed class OrganizationScopeService
{
    private readonly ApplicationDbContext _db;

    public OrganizationScopeService(ApplicationDbContext db) => _db = db;

    public async Task<OrganizationScope?> ResolveAsync(
        Guid? organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (organizationId is null)
            return null;

        var org = await _db.UserAzureDevOpsOrganizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.UserId == userId, cancellationToken);

        if (org is null)
            throw new ArgumentException("Organization was not found.", nameof(organizationId));

        return new OrganizationScope(org.OrganizationDisplay, org.OrganizationKey);
    }

    public static bool Matches(RegisteredRepository repo, OrganizationScope scope) =>
        Matches(repo.AzureDevOpsOrganization, scope);

    public static bool Matches(string azureDevOpsOrganization, OrganizationScope scope) =>
        azureDevOpsOrganization == scope.Display ||
        string.Equals(azureDevOpsOrganization, scope.Key, StringComparison.OrdinalIgnoreCase);

    public IQueryable<RegisteredRepository> FilterRepositories(
        IQueryable<RegisteredRepository> query,
        OrganizationScope scope) =>
        query.Where(r =>
            r.AzureDevOpsOrganization == scope.Display ||
            r.AzureDevOpsOrganization.ToLower() == scope.Key);

    /// <summary>
    /// Releases tied to the org via PRs or enrolled teams' repos, plus empty drafts (no teams/PRs yet).
    /// </summary>
    public async Task<HashSet<Guid>> GetMatchingReleaseIdsAsync(
        OrganizationScope scope,
        CancellationToken cancellationToken)
    {
        var matchingRepoIds = await FilterRepositories(_db.RegisteredRepositories.AsNoTracking(), scope)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var matchingTeamIds = await FilterRepositories(_db.RegisteredRepositories.AsNoTracking(), scope)
            .Select(r => r.TeamId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var fromPrs = await _db.ReleasePullRequests.AsNoTracking()
            .Where(pr => matchingRepoIds.Contains(pr.RegisteredRepositoryId))
            .Select(pr => pr.ReleaseId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var fromTeams = await _db.ReleaseTeams.AsNoTracking()
            .Where(rt => matchingTeamIds.Contains(rt.TeamId))
            .Select(rt => rt.ReleaseId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var emptyDraftIds = await _db.Releases.AsNoTracking()
            .Where(r => !r.Teams.Any() && !r.PullRequests.Any())
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        return fromPrs.Concat(fromTeams).Concat(emptyDraftIds).ToHashSet();
    }
}
