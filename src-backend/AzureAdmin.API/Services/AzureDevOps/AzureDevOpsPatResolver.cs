using System.Collections.Concurrent;

namespace AzureAdmin.API.Services.AzureDevOps;

/// <summary>
/// Resolves per-user PAT credentials stored for each Azure DevOps organization.
/// Scoped: successful lookups are cached for the lifetime of the request/scope, so a batch that touches
/// twenty repositories in one organization decrypts the PAT once instead of twenty times.
/// </summary>
public sealed class AzureDevOpsPatResolver : IAzureDevOpsPatResolver
{
    private readonly AzureDevOpsPatCredentialService _credentials;
    private readonly ConcurrentDictionary<(Guid UserId, string OrganizationKey), string> _cache = new();

    public AzureDevOpsPatResolver(AzureDevOpsPatCredentialService credentials)
    {
        _credentials = credentials;
    }

    public async Task<string> ResolvePatForOrganizationAsync(Guid userId, string organization, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(organization))
            throw new ArgumentException("Organization is required.", nameof(organization));

        var cacheKey = (userId, organization.Trim().ToLowerInvariant());
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var fromStore = await _credentials.TryGetDecryptedPatAsync(userId, organization, cancellationToken);
        if (!string.IsNullOrEmpty(fromStore))
        {
            _cache[cacheKey] = fromStore;
            return fromStore;
        }

        throw new InvalidOperationException(
            $"No valid Azure DevOps PAT is available for organization '{organization.Trim()}' " +
            "(missing, expired, or not saved). Open Settings → Azure organizations, select the org, and add or renew your PAT.");
    }
}
