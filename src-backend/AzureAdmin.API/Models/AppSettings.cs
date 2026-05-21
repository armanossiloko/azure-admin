namespace AzureAdmin.API.Models;

public sealed class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    // Conventional Commits
    public bool ConventionalCommitsEnabled { get; set; }
    public bool ConventionalCommitsUseEmojis { get; set; } = true;

    /// <summary>Comma-separated list of group names to exclude from release notes, e.g. "Chores,Other".</summary>
    public string? ExcludedGroups { get; set; }

    // Jira
    public bool JiraEnabled { get; set; }
    public string? JiraBaseUrl { get; set; }
    public string? JiraProjectKey { get; set; }

    public IReadOnlySet<string> GetExcludedGroupsSet() =>
        string.IsNullOrWhiteSpace(ExcludedGroups)
            ? new HashSet<string>()
            : ExcludedGroups.Split(',')
                .Select(g => g.Trim())
                .Where(g => !string.IsNullOrEmpty(g))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
