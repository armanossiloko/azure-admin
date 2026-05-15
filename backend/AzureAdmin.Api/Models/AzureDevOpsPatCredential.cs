namespace AzureAdmin.Api.Models;

/// <summary>
/// Stores an Azure DevOps PAT encrypted at rest, per signed-in user and Azure DevOps organization.
/// </summary>
public sealed class AzureDevOpsPatCredential
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    /// <summary>Lower-invariant key for lookups; matches URL segment case-insensitively.</summary>
    public string OrganizationKey { get; set; } = "";

    /// <summary>Original casing for display.</summary>
    public string OrganizationDisplay { get; set; } = "";

    public string? DisplayName { get; set; }

    /// <summary>Data-protection encrypted payload (not the raw PAT).</summary>
    public byte[] ProtectedPat { get; set; } = [];

    /// <summary>When the PAT is expected to stop working (from Azure DevOps token settings).</summary>
    public DateTimeOffset? PatExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
