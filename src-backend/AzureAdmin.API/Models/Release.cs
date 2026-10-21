namespace AzureAdmin.API.Models;

public sealed class Release
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? SprintLabel { get; set; }
    public ReleaseLifecycleStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>User who created the release. Null on releases created before this column existed.</summary>
    public Guid? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedBy { get; set; }

    /// <summary>Last change to the release row itself (title, sprint, status). Null until the first edit.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token, regenerated on every change (see <c>ApplicationDbContext.StampReleases</c>).
    /// Clients echo it back on edits so a stale form cannot overwrite someone else's change.
    /// </summary>
    public Guid? ConcurrencyStamp { get; set; }

    public ICollection<ReleaseTeam> Teams { get; set; } = new List<ReleaseTeam>();
    public ICollection<ReleasePullRequest> PullRequests { get; set; } = new List<ReleasePullRequest>();
    public ICollection<ReleaseRepositoryCommitNotes> RepositoryCommitNotes { get; set; } = new List<ReleaseRepositoryCommitNotes>();
}
