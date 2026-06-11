namespace AzureAdmin.API.Contracts.Git;

public sealed record GitBranchDto(
    Guid RegisteredRepositoryId,
    string AzureDevOpsOrganization,
    string AzureDevOpsProject,
    string RepositoryIdOrName,
    string? ServiceName,
    string BranchName,
    string RefName,
    string ObjectId,
    DateTimeOffset? LastCommitDate,
    int? DaysSinceLastCommit,
    bool IsProtected,
    bool IsStale);

public sealed record DeleteGitBranchRequest(
    Guid RegisteredRepositoryId,
    string BranchName);

public sealed record DeleteGitBranchResult(
    bool Success,
    string BranchName,
    string TargetKey,
    string? ErrorMessage);

public sealed record AdminActionLogDto(
    Guid Id,
    Guid UserId,
    string? UserDisplayName,
    string Action,
    string TargetType,
    string TargetKey,
    string? DetailsJson,
    bool Success,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);
