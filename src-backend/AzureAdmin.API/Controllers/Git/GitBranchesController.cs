using System.Net.Http;
using AzureAdmin.API.Contracts.Git;
using AzureAdmin.API.Services.Git;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AzureAdmin.API.Controllers.Git;

[ApiController]
[Authorize]
[Route("api/git/branches")]
public sealed class GitBranchesController : ControllerBase
{
    private readonly StaleBranchService _branches;

    public GitBranchesController(StaleBranchService branches)
    {
        _branches = branches;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GitBranchDto>>> List(
        [FromQuery] Guid? organizationId,
        [FromQuery] Guid? registeredRepositoryId,
        [FromQuery] int? staleDays,
        [FromQuery] bool staleOnly = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _branches.ListBranchesAsync(
                organizationId,
                registeredRepositoryId,
                staleDays,
                staleOnly,
                cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
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

    [HttpDelete]
    public async Task<ActionResult<DeleteGitBranchResult>> Delete(
        [FromBody] DeleteGitBranchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _branches.DeleteBranchAsync(request, cancellationToken);
            if (!result.Success)
                return BadRequest(result);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
