namespace AzureAdmin.API.Models;

/// <summary>Cached commits between two branches, captured when a release batch PR is created.</summary>
public sealed class ReleaseRepositoryCommitNotes
{
    public Guid Id { get; set; }

    public Guid ReleaseId { get; set; }
    public Release Release { get; set; } = null!;

    public Guid RegisteredRepositoryId { get; set; }
    public RegisteredRepository RegisteredRepository { get; set; } = null!;

    public ReleasePrPhase Phase { get; set; }

    public string SourceRefName { get; set; } = "";
    public string TargetRefName { get; set; } = "";

    /// <summary>JSON array of commit entries (see API DTOs).</summary>
    public string CommitsJson { get; set; } = "[]";

    public DateTimeOffset FetchedAt { get; set; }
}
