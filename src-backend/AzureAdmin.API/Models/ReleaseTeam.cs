namespace AzureAdmin.API.Models;

/// <summary>
/// Associates a team with a release. One release record is shared; each team works its own repos and PRs.
/// </summary>
public sealed class ReleaseTeam
{
    public Guid Id { get; set; }
    public Guid ReleaseId { get; set; }
    public Release Release { get; set; } = null!;
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;
}
