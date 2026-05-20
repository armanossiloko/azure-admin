using System.Net.Http;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Services.AzureDevOps;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AzureAdmin.API.Controllers.AzureDevOps;

[ApiController]
[Authorize]
[Route("api/azure-devops/catalog")]
public sealed class AzureDevOpsCatalogController : ControllerBase
{
    private readonly AzureDevOpsCatalogService _catalog;

    public AzureDevOpsCatalogController(AzureDevOpsCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("organizations/{organizationId:guid}/projects")]
    public async Task<ActionResult<IReadOnlyList<AzureCatalogProjectDto>>> ListProjects(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _catalog.ListProjectsAsync(organizationId, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("organizations/{organizationId:guid}/repositories")]
    public async Task<ActionResult<IReadOnlyList<AzureCatalogRepositoryDto>>> ListRepositories(
        Guid organizationId,
        [FromQuery(Name = "project")] string projectName,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _catalog.ListRepositoriesAsync(organizationId, projectName, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return string.Equals(ex.ParamName, nameof(projectName), StringComparison.Ordinal)
                ? BadRequest(new { message = ex.Message })
                : NotFound(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
