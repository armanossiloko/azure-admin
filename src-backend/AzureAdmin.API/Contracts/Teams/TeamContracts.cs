namespace AzureAdmin.API.Contracts;

public sealed record TeamDto(Guid Id, string Name, Guid? ParentTeamId);

public sealed record CreateTeamRequest(string Name, Guid? ParentTeamId);

/// <param name="Name">When null, name is left unchanged.</param>
/// <param name="ParentTeamId">When omitted, parent is left unchanged. Pass null explicitly via JSON null to clear.</param>
/// <param name="ClearParent">When true, clears the parent regardless of <paramref name="ParentTeamId"/>.</param>
public sealed record UpdateTeamRequest(
    string? Name = null,
    Guid? ParentTeamId = null,
    bool ClearParent = false);
