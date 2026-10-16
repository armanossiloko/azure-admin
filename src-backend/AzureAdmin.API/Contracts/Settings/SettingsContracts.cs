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
    string? JiraProjectKey = null,
    /// <summary>Stored branding override; null means use config / built-in defaults.</summary>
    string? BrandingApplicationName = null,
    string? BrandingTagline = null,
    string? BrandingDescription = null,
    /// <summary>Resolved branding after overrides + config + defaults.</summary>
    BrandingDto? Branding = null);

/// <param name="SprintWeeks">Optional so that an older client that does not send it leaves the setting unchanged.</param>
/// <param name="JiraProjectKeys">Preferred multi-key list. When null, <paramref name="JiraProjectKey"/> is used if present; when both are null the stored keys are left unchanged.</param>
/// <param name="JiraProjectKey">Legacy single project key from older clients.</param>
/// <param name="BrandingApplicationName">When null, stored branding name is left unchanged. Empty string clears the override.</param>
/// <param name="BrandingTagline">When null, stored tagline is left unchanged. Empty string clears the override.</param>
/// <param name="BrandingDescription">When null, stored description is left unchanged. Empty string clears the override.</param>
public sealed record UpdateAppSettingsRequest(
    bool ConventionalCommitsEnabled,
    bool ConventionalCommitsUseEmojis,
    IReadOnlyList<string> ExcludedGroups,
    bool JiraEnabled,
    string? JiraBaseUrl,
    IReadOnlyList<string>? JiraProjectKeys = null,
    string? JiraProjectKey = null,
    int? SprintWeeks = null,
    string? BrandingApplicationName = null,
    string? BrandingTagline = null,
    string? BrandingDescription = null);
