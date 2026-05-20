namespace AzureAdmin.API.Contracts;

public sealed record AzureDevOpsOrganizationSummaryDto(
    Guid Id,
    string OrganizationKey,
    string OrganizationDisplay,
    string? Notes,
    bool HasPatCredential,
    Guid? PatCredentialId,
    DateTimeOffset? PatUpdatedAt,
    DateTimeOffset? PatExpiresAt);

public sealed class UpsertOrganizationPatCredentialRequest
{
    public string Pat { get; set; } = "";

    /// <summary>When this PAT expires in Azure DevOps (must be in the future).</summary>
    public DateTimeOffset PatExpiresAt { get; set; }
}

public sealed class CreateAzureDevOpsOrganizationRequest
{
    /// <summary>Azure DevOps organization name (URL segment), e.g. <c>contoso</c>.</summary>
    public string Organization { get; set; } = "";

    /// <summary>Optional friendly label; must be the same org (same normalized key) as <see cref="Organization"/>.</summary>
    public string? OrganizationDisplay { get; set; }

    public string? Notes { get; set; }
}

public sealed class UpdateAzureDevOpsOrganizationRequest
{
    public string? Notes { get; set; }

    /// <summary>When set, updates display casing only (key stays the same).</summary>
    public string? OrganizationDisplay { get; set; }
}
