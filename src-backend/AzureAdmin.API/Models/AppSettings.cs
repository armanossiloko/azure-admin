namespace AzureAdmin.API.Models;

public sealed class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    // Conventional Commits
    public bool ConventionalCommitsEnabled { get; set; }
    public bool ConventionalCommitsUseEmojis { get; set; } = true;
    public bool ConventionalCommitsShowOtherGroup { get; set; } = true;

    // Jira
    public bool JiraEnabled { get; set; }
    public string? JiraBaseUrl { get; set; }
    public string? JiraProjectKey { get; set; }
}
