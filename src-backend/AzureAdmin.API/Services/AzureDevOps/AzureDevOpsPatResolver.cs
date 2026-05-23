namespace AzureAdmin.API.Services.AzureDevOps;

/// <summary>
/// Resolves per-user PAT credentials stored for each Azure DevOps organization.
/// </summary>
public sealed class AzureDevOpsPatResolver : IAzureDevOpsPatResolver
{
    private readonly AzureDevOpsPatCredentialService _credentials;

    public AzureDevOpsPatResolver(AzureDevOpsPatCredentialService credentials)
    {
        _credentials = credentials;
    }

    public async Task<string> ResolvePatForOrganizationAsync(Guid userId, string organization, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(organization))
            throw new ArgumentException("Organization is required.", nameof(organization));

        var fromStore = await _credentials.TryGetDecryptedPatAsync(userId, organization, cancellationToken);
        if (!string.IsNullOrEmpty(fromStore))
            return fromStore;

        throw new InvalidOperationException(
            $"No valid Azure DevOps PAT is available for organization '{organization.Trim()}' " +
            "(missing, expired, or not saved). Open Settings → Azure organizations, select the org, and add or renew your PAT.");
    }
}
