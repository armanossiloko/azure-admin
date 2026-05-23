namespace AzureAdmin.API.Models;

public sealed class Team
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? ParentTeamId { get; set; }

    public Team? Parent { get; set; }
    public ICollection<Team> Children { get; set; } = new List<Team>();
    public ICollection<RegisteredRepository> RegisteredRepositories { get; set; } = new List<RegisteredRepository>();
    public ICollection<ReleaseTeam> ReleaseTeams { get; set; } = new List<ReleaseTeam>();
    public ICollection<ReleasePullRequest> ReleasePullRequests { get; set; } = new List<ReleasePullRequest>();
}
