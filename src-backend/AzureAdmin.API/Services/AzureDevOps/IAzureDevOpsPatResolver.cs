namespace AzureAdmin.API.Services.AzureDevOps;

public interface IAzureDevOpsPatResolver
{
    /// <summary>Resolves the PAT for the given user and Azure DevOps organization.</summary>
    Task<string> ResolvePatForOrganizationAsync(Guid userId, string organization, CancellationToken cancellationToken);
}
