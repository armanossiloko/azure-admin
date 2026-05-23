namespace AzureAdmin.API.Contracts;

/// <summary>Azure DevOps Team Project (from Core REST).</summary>
public sealed record AzureCatalogProjectDto(string Id, string Name);

/// <summary>Git repository in a project (from Git REST).</summary>
public sealed record AzureCatalogRepositoryDto(string Id, string Name, string ProjectName);
