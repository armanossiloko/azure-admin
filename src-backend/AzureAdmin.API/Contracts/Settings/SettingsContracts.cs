namespace AzureAdmin.API.Contracts;

public sealed record AppSettingsDto(
    bool ConventionalCommitsEnabled,
    bool ConventionalCommitsUseEmojis,
    IReadOnlyList<string> ExcludedGroups,
    int SprintWeeks,
    bool JiraEnabled,
    string? JiraBaseUrl,
    IReadOnlyList<string> JiraProjectKeys,
    /// <summary>Legacy single value (comma-joined when multiple). Kept so older clients keep working.</summary>
    string? JiraProjectKey = null);

/// <param name="SprintWeeks">Optional so that an older client that does not send it leaves the setting unchanged.</param>
/// <param name="JiraProjectKeys">Preferred multi-key list. When null, <paramref name="JiraProjectKey"/> is used if present; when both are null the stored keys are left unchanged.</param>
/// <param name="JiraProjectKey">Legacy single project key from older clients.</param>
public sealed record UpdateAppSettingsRequest(
    bool ConventionalCommitsEnabled,
    bool ConventionalCommitsUseEmojis,
    IReadOnlyList<string> ExcludedGroups,
    bool JiraEnabled,
    string? JiraBaseUrl,
    IReadOnlyList<string>? JiraProjectKeys = null,
    string? JiraProjectKey = null,
    int? SprintWeeks = null);
