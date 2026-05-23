using AzureAdmin.API.Contracts;
using AzureAdmin.API.Services.AzureDevOps;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AzureAdmin.API.Controllers.AzureDevOps;

[ApiController]
[Authorize]
[Route("api/azure-devops/organizations")]
public sealed class AzureDevOpsOrganizationsController : ControllerBase
{
    private readonly AzureDevOpsOrganizationService _organizations;
    private readonly AzureDevOpsPatCredentialService _patCredentials;

    public AzureDevOpsOrganizationsController(
        AzureDevOpsOrganizationService organizations,
        AzureDevOpsPatCredentialService patCredentials)
    {
        _organizations = organizations;
        _patCredentials = patCredentials;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AzureDevOpsOrganizationSummaryDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _organizations.ListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AzureDevOpsOrganizationSummaryDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var row = await _organizations.GetAsync(id, cancellationToken);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost]
    public async Task<ActionResult<AzureDevOpsOrganizationSummaryDto>> Create(
        [FromBody] CreateAzureDevOpsOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _organizations.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AzureDevOpsOrganizationSummaryDto>> Update(
        Guid id,
        [FromBody] UpdateAzureDevOpsOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _organizations.UpdateAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var ok = await _organizations.DeleteAsync(id, cancellationToken);
        return ok ? NoContent() : NotFound();
    }

    [HttpPut("{id:guid}/pat-credential")]
    public async Task<ActionResult<AzureDevOpsOrganizationSummaryDto>> UpsertPatCredential(
        Guid id,
        [FromBody] UpsertOrganizationPatCredentialRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _patCredentials.UpsertPatForOrganizationAsync(id, request, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return string.Equals(ex.ParamName, "organizationId", StringComparison.Ordinal)
                ? NotFound(new { message = ex.Message })
                : BadRequest(new { message = ex.Message });
        }

        var row = await _organizations.GetAsync(id, cancellationToken);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpDelete("{id:guid}/pat-credential")]
    public async Task<ActionResult<AzureDevOpsOrganizationSummaryDto>> DeletePatCredential(
        Guid id,
        CancellationToken cancellationToken)
    {
        var ok = await _patCredentials.DeletePatForOrganizationAsync(id, cancellationToken);
        if (!ok)
            return NotFound();

        var row = await _organizations.GetAsync(id, cancellationToken);
        return row is null ? NotFound() : Ok(row);
    }
}
