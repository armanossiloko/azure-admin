namespace AzureAdmin.Api.Models;

/// <summary>
/// A repo in Azure DevOps assigned to exactly one team (typically a leaf subteam).
/// </summary>
public sealed class RegisteredRepository
{
    public Guid Id { get; set; }
    public string AzureDevOpsOrganization { get; set; } = "";
    public string AzureDevOpsProject { get; set; } = "";
    public string RepositoryIdOrName { get; set; } = "";
    /// <summary>Optional display name for the service or API.</summary>
    public string? ServiceName { get; set; }

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public ICollection<ReleasePullRequest> ReleasePullRequests { get; set; } = new List<ReleasePullRequest>();
    public ICollection<ReleaseRepositoryCommitNotes> ReleaseRepositoryCommitNotes { get; set; } = new List<ReleaseRepositoryCommitNotes>();
}
