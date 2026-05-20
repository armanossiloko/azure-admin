namespace AzureAdmin.API.Contracts;

public sealed record TeamDto(Guid Id, string Name, Guid? ParentTeamId);

public sealed record CreateTeamRequest(string Name, Guid? ParentTeamId);
