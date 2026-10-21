namespace AzureAdmin.API.Services.Releases;

/// <summary>A user holding a non-expired PAT for an organization (lower-case key).</summary>
public sealed record PatHolder(Guid UserId, string OrganizationKey, DateTimeOffset UpdatedAt);

/// <summary>
/// Decides whose PAT the background sync uses for a pull request. PATs are per user, and a PR row only
/// knows who opened it (and not even that for rows older than the column), so: prefer the creator when
/// they still hold a valid PAT for the organization, otherwise the holder who most recently saved one.
/// </summary>
public static class PullRequestSyncUserPicker
{
    public static Guid? Pick(Guid? createdByUserId, string organization, IReadOnlyCollection<PatHolder> holders)
    {
        var orgKey = organization.Trim().ToLowerInvariant();
        var forOrg = holders.Where(h => h.OrganizationKey == orgKey).ToList();
        if (forOrg.Count == 0)
            return null;

        if (createdByUserId is { } creator && forOrg.Any(h => h.UserId == creator))
            return creator;

        return forOrg.MaxBy(h => h.UpdatedAt)!.UserId;
    }
}
