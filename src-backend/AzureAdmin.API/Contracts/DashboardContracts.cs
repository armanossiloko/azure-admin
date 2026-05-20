namespace AzureAdmin.API.Contracts;

public sealed record DashboardDto(
    DashboardStatsDto Stats,
    DashboardChecklistDto Checklist,
    IReadOnlyList<DashboardReleaseSummaryDto> ActiveReleaseHighlights,
    IReadOnlyList<DashboardActivityDto> RecentActivity);

public sealed record DashboardReleaseSummaryDto(
    Guid Id,
    string Title,
    string? SprintLabel,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record DashboardStatsDto(
    int ActiveReleasesCount,
    int OpenPullRequestsCount,
    int PullRequestsNeedingAttentionCount,
    int RegisteredRepositoriesCount,
    int DistinctAzureDevOpsProjectsCount);

public sealed record DashboardChecklistDto(
    bool HasAzureOrganization,
    bool HasTeam,
    bool HasRegisteredRepository,
    bool HasRelease);

/// <summary>Timeline item for overview (releases, PRs, org connections).</summary>
public sealed record DashboardActivityDto(
    string Kind,
    string Title,
    string? Subtitle,
    DateTimeOffset OccurredAt,
    string? Href);

public sealed record NavigationSummaryDto(
    IReadOnlyList<NavigationOrganizationDto> Organizations,
    IReadOnlyList<DashboardActivityDto> ActivityPreview,
    int UnreadNotificationsCount);

public sealed record NavigationOrganizationDto(
    Guid Id,
    string DisplayName,
    string OrganizationKey,
    bool HasPatCredential);
