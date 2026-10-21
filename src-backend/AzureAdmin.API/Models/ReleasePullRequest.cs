namespace AzureAdmin.API.Models;

public sealed class ReleasePullRequest
{
    public Guid Id { get; set; }
    public Guid ReleaseId { get; set; }
    public Release Release { get; set; } = null!;
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;
    public Guid RegisteredRepositoryId { get; set; }
    public RegisteredRepository RegisteredRepository { get; set; } = null!;

    public ReleasePrPhase Phase { get; set; }
    public ReleasePullRequestStatus Status { get; set; } = ReleasePullRequestStatus.Active;
    public int AzureDevOpsPullRequestId { get; set; }
    public string Url { get; set; } = "";
    public string SourceRefName { get; set; } = "";
    public string TargetRefName { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// User whose PAT opened the PR. Null on rows created before this column existed; the background
    /// status sync then falls back to any user holding a valid PAT for the organization.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }
}
