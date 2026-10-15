using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Controllers.Teams;

[ApiController]
[Authorize]
[Route("api/teams")]
public sealed class TeamsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public TeamsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TeamDto>>> List(CancellationToken cancellationToken)
    {
        var rows = await _db.Teams
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TeamDto(t.Id, t.Name, t.ParentTeamId))
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TeamDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.Teams
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TeamDto(t.Id, t.Name, t.ParentTeamId))
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost]
    public async Task<ActionResult<TeamDto>> Create([FromBody] CreateTeamRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        if (request.ParentTeamId is { } parentId)
        {
            var parentExists = await _db.Teams.AnyAsync(t => t.Id == parentId, cancellationToken);
            if (!parentExists)
                return BadRequest("Parent team was not found.");
        }

        var entity = new Team
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            ParentTeamId = request.ParentTeamId
        };

        _db.Teams.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = entity.Id }, new TeamDto(entity.Id, entity.Name, entity.ParentTeamId));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<TeamDto>> Update(
        Guid id,
        [FromBody] UpdateTeamRequest request,
        CancellationToken cancellationToken)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (team is null)
            return NotFound();

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "Name cannot be empty." });
            team.Name = request.Name.Trim();
        }

        if (request.ClearParent)
        {
            team.ParentTeamId = null;
        }
        else if (request.ParentTeamId is { } parentId)
        {
            if (parentId == id)
                return BadRequest(new { message = "A team cannot be its own parent." });

            var parentExists = await _db.Teams.AnyAsync(t => t.Id == parentId, cancellationToken);
            if (!parentExists)
                return BadRequest(new { message = "Parent team was not found." });

            // Prevent simple cycles: parent cannot be a descendant of this team.
            if (await IsDescendantAsync(parentId, id, cancellationToken))
                return BadRequest(new { message = "Cannot set a descendant team as the parent." });

            team.ParentTeamId = parentId;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new TeamDto(team.Id, team.Name, team.ParentTeamId));
    }

    private async Task<bool> IsDescendantAsync(Guid candidateId, Guid ancestorId, CancellationToken cancellationToken)
    {
        var current = candidateId;
        var guard = 0;
        while (guard++ < 64)
        {
            var parentId = await _db.Teams.AsNoTracking()
                .Where(t => t.Id == current)
                .Select(t => t.ParentTeamId)
                .FirstOrDefaultAsync(cancellationToken);

            if (parentId is null)
                return false;
            if (parentId == ancestorId)
                return true;
            current = parentId.Value;
        }

        return false;
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var team = await _db.Teams
            .Include(t => t.Children)
            .Include(t => t.RegisteredRepositories)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (team is null)
            return NotFound();

        if (team.Children.Count > 0)
            return Conflict("Cannot delete a team that has child teams.");

        if (team.RegisteredRepositories.Count > 0)
            return Conflict("Cannot delete a team that still has repositories assigned. Remove or reassign repositories first.");

        var usedInRelease = await _db.ReleaseTeams.AnyAsync(rt => rt.TeamId == id, cancellationToken)
            || await _db.ReleasePullRequests.AnyAsync(pr => pr.TeamId == id, cancellationToken);

        if (usedInRelease)
            return Conflict("Cannot delete a team that is referenced by releases.");

        _db.Teams.Remove(team);
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
