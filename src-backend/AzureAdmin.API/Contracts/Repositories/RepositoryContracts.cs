namespace AzureAdmin.API.Contracts;

public sealed record RegisteredRepositoryDto(
    Guid Id,
    string AzureDevOpsOrganization,
    string AzureDevOpsProject,
    string RepositoryIdOrName,
    string? ServiceName,
    Guid TeamId,
    bool IsDecommissioned);

public sealed record RegisterRepositoryRequest(
    string AzureDevOpsOrganization,
    string AzureDevOpsProject,
    string RepositoryIdOrName,
    string? ServiceName,
    Guid TeamId);

public sealed record PatchRegisteredRepositoryRequest(string? ServiceName, bool? IsDecommissioned);
