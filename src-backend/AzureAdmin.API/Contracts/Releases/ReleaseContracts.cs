using AzureAdmin.API.Models;

namespace AzureAdmin.API.Contracts;

public sealed record ReleaseSummaryDto(
    Guid Id,
    string Title,
    string? SprintLabel,
    ReleaseLifecycleStatus Status,
    DateTimeOffset CreatedAt);

public sealed record CreateReleaseRequest(string Title, string? SprintLabel);

public sealed record ReleaseDetailDto(
    Guid Id,
    string Title,
    string? SprintLabel,
    ReleaseLifecycleStatus Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReleaseTeamDto> Teams,
    IReadOnlyList<ReleasePullRequestDto> PullRequests,
    IReadOnlyList<ReleaseRepositoryCommitNotesDto> RepositoryCommitNotes);

public sealed record ReleaseTeamDto(Guid Id, Guid TeamId, string TeamName);

public sealed record ReleasePullRequestDto(
    Guid Id,
    Guid TeamId,
    string TeamName,
    Guid RegisteredRepositoryId,
    string? ServiceName,
    string RepositoryIdOrName,
    ReleasePrPhase Phase,
    ReleasePullRequestStatus Status,
    int AzureDevOpsPullRequestId,
    string Url,
    string SourceRefName,
    string TargetRefName,
    string Title,
    DateTimeOffset CreatedAt);

public sealed record BatchCreateReleasePullRequestsRequest(
    ReleasePrPhase Phase,
    string Title,
    string? Description,
    /// <summary>When omitted, defaults to dev (DevToMaster) or master (MasterToProd).</summary>
    string? SourceBranch,
    /// <summary>When omitted, defaults to master (DevToMaster) or prod (MasterToProd).</summary>
    string? TargetBranch,
    IReadOnlyList<Guid> RegisteredRepositoryIds);

public sealed record BatchCreateReleasePullRequestsResponse(IReadOnlyList<CreatedPullRequestResult> Results);

public sealed record CreatedPullRequestResult(string RepositoryIdOrName, int PullRequestId, string Url);

public sealed record AddTeamToReleaseRequest(Guid TeamId);

public sealed record CompletePullRequestsBatchRequest(ReleasePrPhase Phase);

public sealed record CompletePullRequestsBatchResponse(IReadOnlyList<CompletedPullRequestResult> Results);

public sealed record CompletedPullRequestResult(
    string RepositoryIdOrName,
    int PullRequestId,
    bool Success,
    string? Message);

public sealed record RefreshPullRequestStatusesRequest(ReleasePrPhase? Phase);

public sealed record RefreshPullRequestStatusesResponse(IReadOnlyList<ReleasePullRequestStatusResult> Results);

public sealed record ReleasePullRequestStatusResult(Guid PullRequestId, ReleasePullRequestStatus Status);

public sealed record ReleaseCommitItemDto(string CommitId, string Comment, string AuthorName, DateTimeOffset CommittedDate);

public sealed record JiraTicketRefDto(string Key, string Url);

public sealed record EnrichedCommitItemDto(
    string CommitId,
    string AuthorName,
    DateTimeOffset CommittedDate,
    string RawComment,
    string? ConventionalType,
    string? Scope,
    string Description,
    bool IsBreaking,
    IReadOnlyList<JiraTicketRefDto> JiraReferences);

public sealed record CommitGroupDto(
    string GroupName,
    bool IsBreaking,
    IReadOnlyList<EnrichedCommitItemDto> Commits);

public sealed record ReleaseRepositoryCommitNotesDto(
    Guid RegisteredRepositoryId,
    string? ServiceName,
    string RepositoryIdOrName,
    ReleasePrPhase Phase,
    string SourceRefName,
    string TargetRefName,
    DateTimeOffset FetchedAt,
    IReadOnlyList<ReleaseCommitItemDto> Commits,
    IReadOnlyList<CommitGroupDto>? CommitGroups = null);
