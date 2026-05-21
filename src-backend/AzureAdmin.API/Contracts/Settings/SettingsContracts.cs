namespace AzureAdmin.API.Contracts;

public sealed record AppSettingsDto(
    bool ConventionalCommitsEnabled,
    bool ConventionalCommitsUseEmojis,
    bool ConventionalCommitsShowOtherGroup,
    bool JiraEnabled,
    string? JiraBaseUrl,
    string? JiraProjectKey);

public sealed record UpdateAppSettingsRequest(
    bool ConventionalCommitsEnabled,
    bool ConventionalCommitsUseEmojis,
    bool ConventionalCommitsShowOtherGroup,
    bool JiraEnabled,
    string? JiraBaseUrl,
    string? JiraProjectKey);
