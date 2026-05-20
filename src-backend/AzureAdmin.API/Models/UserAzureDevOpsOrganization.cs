namespace AzureAdmin.API.Models;

/// <summary>
/// A DevOps organization URL segment (<c>dev.azure.com/{organization}</c>) the user works with.
/// PAT credentials attach to the same normalized key; PRs are created with that user's PAT for the repo's org.
/// </summary>
public sealed class UserAzureDevOpsOrganization
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    /// <summary>Lower-invariant key; matches <see cref="AzureDevOpsPatCredential.OrganizationKey"/> and repo URLs.</summary>
    public string OrganizationKey { get; set; } = "";

    /// <summary>Human-friendly casing for UI.</summary>
    public string OrganizationDisplay { get; set; } = "";

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
