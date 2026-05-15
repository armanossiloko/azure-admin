namespace AzureAdmin.Api.Models;

public sealed class Release
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? SprintLabel { get; set; }
    public ReleaseLifecycleStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<ReleaseTeam> Teams { get; set; } = new List<ReleaseTeam>();
    public ICollection<ReleasePullRequest> PullRequests { get; set; } = new List<ReleasePullRequest>();
    public ICollection<ReleaseRepositoryCommitNotes> RepositoryCommitNotes { get; set; } = new List<ReleaseRepositoryCommitNotes>();
}
