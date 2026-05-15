using AzureAdmin.Api.Contracts;
using AzureAdmin.Api.Data;
using AzureAdmin.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.Api.Controllers.Repositories;

[ApiController]
[Authorize]
[Route("api/registered-repositories")]
public sealed class RegisteredRepositoriesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public RegisteredRepositoriesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RegisteredRepositoryDto>>> List(
        [FromQuery] Guid? teamId,
        CancellationToken cancellationToken)
    {
        var query = _db.RegisteredRepositories.AsNoTracking().AsQueryable();
        if (teamId is { } tid)
            query = query.Where(r => r.TeamId == tid);

        var rows = await query
            .OrderBy(r => r.AzureDevOpsOrganization)
            .ThenBy(r => r.AzureDevOpsProject)
            .ThenBy(r => r.RepositoryIdOrName)
            .Select(r => new RegisteredRepositoryDto(
                r.Id,
                r.AzureDevOpsOrganization,
                r.AzureDevOpsProject,
                r.RepositoryIdOrName,
                r.ServiceName,
                r.TeamId))
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<RegisteredRepositoryDto>> Register(
        [FromBody] RegisterRepositoryRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AzureDevOpsOrganization))
            return BadRequest("AzureDevOpsOrganization is required.");
        if (string.IsNullOrWhiteSpace(request.AzureDevOpsProject))
            return BadRequest("AzureDevOpsProject is required.");
        if (string.IsNullOrWhiteSpace(request.RepositoryIdOrName))
            return BadRequest("RepositoryIdOrName is required.");

        var teamExists = await _db.Teams.AnyAsync(t => t.Id == request.TeamId, cancellationToken);
        if (!teamExists)
            return BadRequest("Team was not found.");

        var entity = new RegisteredRepository
        {
            Id = Guid.NewGuid(),
            AzureDevOpsOrganization = request.AzureDevOpsOrganization.Trim(),
            AzureDevOpsProject = request.AzureDevOpsProject.Trim(),
            RepositoryIdOrName = request.RepositoryIdOrName.Trim(),
            ServiceName = string.IsNullOrWhiteSpace(request.ServiceName) ? null : request.ServiceName.Trim(),
            TeamId = request.TeamId
        };

        try
        {
            _db.RegisteredRepositories.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("This Azure DevOps repository is already registered.");
        }

        return Ok(new RegisteredRepositoryDto(
            entity.Id,
            entity.AzureDevOpsOrganization,
            entity.AzureDevOpsProject,
            entity.RepositoryIdOrName,
            entity.ServiceName,
            entity.TeamId));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<RegisteredRepositoryDto>> Patch(
        Guid id,
        [FromBody] PatchRegisteredRepositoryRequest request,
        CancellationToken cancellationToken)
    {
        var row = await _db.RegisteredRepositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (row is null)
            return NotFound();

        if (request.ServiceName is not null)
            row.ServiceName = string.IsNullOrWhiteSpace(request.ServiceName) ? null : request.ServiceName.Trim();

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new RegisteredRepositoryDto(
            row.Id,
            row.AzureDevOpsOrganization,
            row.AzureDevOpsProject,
            row.RepositoryIdOrName,
            row.ServiceName,
            row.TeamId));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.RegisteredRepositories
            .Include(r => r.ReleasePullRequests)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (row is null)
            return NotFound();

        if (row.ReleasePullRequests.Count > 0)
            return Conflict("Cannot remove a repository that already has release pull requests recorded.");

        _db.RegisteredRepositories.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
