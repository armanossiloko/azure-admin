namespace AzureAdmin.API.Contracts;

public sealed record AppSettingsDto(
    bool ConventionalCommitsEnabled,
    bool ConventionalCommitsUseEmojis,
    IReadOnlyList<string> ExcludedGroups,
    int SprintWeeks,
    bool JiraEnabled,
    string? JiraBaseUrl,
    string? JiraProjectKey);

/// <param name="SprintWeeks">Optional so that an older client that does not send it leaves the setting unchanged.</param>
public sealed record UpdateAppSettingsRequest(
    bool ConventionalCommitsEnabled,
    bool ConventionalCommitsUseEmojis,
    IReadOnlyList<string> ExcludedGroups,
    bool JiraEnabled,
    string? JiraBaseUrl,
    string? JiraProjectKey,
    int? SprintWeeks = null);
