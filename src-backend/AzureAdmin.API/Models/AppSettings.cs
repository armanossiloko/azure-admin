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

    /// <summary>
    /// Sprint size in weeks. The default sprint label of a new release is the calendar week this many weeks
    /// back, because a release is created at the end of a sprint. 0 uses the current week.
    /// </summary>
    public int SprintWeeks { get; set; }

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
