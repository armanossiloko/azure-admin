using System.Net.Http;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Identity;
using AzureAdmin.API.Services.Releases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Controllers.Releases;

[ApiController]
[Authorize]
[Route("api/releases")]
public sealed class ReleasesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ReleasePullRequestBatchService _batchService;
    private readonly ReleaseCommitNotesService _commitNotes;
    private readonly ICurrentUser _currentUser;
    private readonly ConventionalCommitParser _commitParser;
    private readonly JiraReferenceExtractor _jiraExtractor;

    public ReleasesController(
        ApplicationDbContext db,
        ReleasePullRequestBatchService batchService,
        ReleaseCommitNotesService commitNotes,
        ICurrentUser currentUser,
        ConventionalCommitParser commitParser,
        JiraReferenceExtractor jiraExtractor)
    {
        _db = db;
        _batchService = batchService;
        _commitNotes = commitNotes;
        _currentUser = currentUser;
        _commitParser = commitParser;
        _jiraExtractor = jiraExtractor;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReleaseSummaryDto>>> List(CancellationToken cancellationToken)
    {
        var rows = await _db.Releases
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReleaseSummaryDto(r.Id, r.Title, r.SprintLabel, r.Status, r.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpPost("find-or-create")]
    public async Task<ActionResult<ReleaseSummaryDto>> FindOrCreateRelease(
        [FromBody] CreateReleaseRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title is required.");

        var title = request.Title.Trim();
        var sprint = string.IsNullOrWhiteSpace(request.SprintLabel) ? null : request.SprintLabel.Trim();

        var existing = await _db.Releases.AsNoTracking()
            .Where(r => r.Status == ReleaseLifecycleStatus.Draft)
            .Where(r => EF.Functions.ILike(r.Title, title))
            .Where(r =>
                (sprint == null && r.SprintLabel == null) ||
                (sprint != null && r.SprintLabel != null && EF.Functions.ILike(r.SprintLabel, sprint)))
            .OrderBy(r => r.CreatedAt)
            .Select(r => new ReleaseSummaryDto(r.Id, r.Title, r.SprintLabel, r.Status, r.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
            return Ok(existing);

        var entity = new Release
        {
            Id = Guid.NewGuid(),
            Title = title,
            SprintLabel = sprint,
            Status = ReleaseLifecycleStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.Releases.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        var dto = new ReleaseSummaryDto(entity.Id, entity.Title, entity.SprintLabel, entity.Status, entity.CreatedAt);
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, dto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReleaseDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var release = await _db.Releases
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new
            {
                r.Id,
                r.Title,
                r.SprintLabel,
                r.Status,
                r.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (release is null)
            return NotFound();

        var teams = await _db.ReleaseTeams
            .AsNoTracking()
            .Where(rt => rt.ReleaseId == id)
            .Include(rt => rt.Team)
            .Select(rt => new ReleaseTeamDto(rt.Id, rt.TeamId, rt.Team.Name))
            .ToListAsync(cancellationToken);

        var prs = await _db.ReleasePullRequests
            .AsNoTracking()
            .Where(pr => pr.ReleaseId == id)
            .Include(pr => pr.Team)
            .Include(pr => pr.RegisteredRepository)
            .OrderByDescending(pr => pr.CreatedAt)
            .Select(pr => new ReleasePullRequestDto(
                pr.Id,
                pr.TeamId,
                pr.Team.Name,
                pr.RegisteredRepositoryId,
                pr.RegisteredRepository.ServiceName,
                pr.RegisteredRepository.RepositoryIdOrName,
                pr.Phase,
                pr.AzureDevOpsPullRequestId,
                pr.Url,
                pr.SourceRefName,
                pr.TargetRefName,
                pr.Title,
                pr.CreatedAt))
            .ToListAsync(cancellationToken);

        var noteRows = await _db.ReleaseRepositoryCommitNotes
            .AsNoTracking()
            .Where(n => n.ReleaseId == id)
            .Include(n => n.RegisteredRepository)
            .OrderBy(n => n.RegisteredRepository.RepositoryIdOrName)
            .ThenBy(n => n.Phase)
            .ToListAsync(cancellationToken);

        var appSettings = await _db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == AppSettings.SingletonId, cancellationToken)
            ?? new AppSettings();

        var notesDtos = new List<ReleaseRepositoryCommitNotesDto>();
        foreach (var n in noteRows)
        {
            var commits = ReleaseCommitJson.Deserialize(n.CommitsJson);
            var commitGroups = BuildCommitGroups(commits, appSettings);
            notesDtos.Add(new ReleaseRepositoryCommitNotesDto(
                n.RegisteredRepositoryId,
                n.RegisteredRepository.ServiceName,
                n.RegisteredRepository.RepositoryIdOrName,
                n.Phase,
                n.SourceRefName,
                n.TargetRefName,
                n.FetchedAt,
                commits,
                commitGroups));
        }

        return Ok(new ReleaseDetailDto(
            release.Id,
            release.Title,
            release.SprintLabel,
            release.Status,
            release.CreatedAt,
            teams,
            prs,
            notesDtos));
    }

    [HttpPost]
    public async Task<ActionResult<ReleaseSummaryDto>> Create(
        [FromBody] CreateReleaseRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title is required.");

        var entity = new Release
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            SprintLabel = string.IsNullOrWhiteSpace(request.SprintLabel) ? null : request.SprintLabel.Trim(),
            Status = ReleaseLifecycleStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.Releases.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        var dto = new ReleaseSummaryDto(entity.Id, entity.Title, entity.SprintLabel, entity.Status, entity.CreatedAt);
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, dto);
    }

    [HttpPost("{releaseId:guid}/teams/{teamId:guid}/pull-requests/batch")]
    public async Task<ActionResult<BatchCreateReleasePullRequestsResponse>> BatchCreatePullRequests(
        Guid releaseId,
        Guid teamId,
        [FromBody] BatchCreateReleasePullRequestsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await _batchService.CreatePullRequestsForReleaseAsync(
                releaseId,
                teamId,
                request,
                cancellationToken);

            return Ok(new BatchCreateReleasePullRequestsResponse(results));
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

    [HttpPost("{releaseId:guid}/commit-notes/refresh")]
    public async Task<IActionResult> RefreshCommitNotes(Guid releaseId, CancellationToken cancellationToken)
    {
        if (!await _db.Releases.AnyAsync(r => r.Id == releaseId, cancellationToken))
            return NotFound();

        var userId = _currentUser.GetRequiredUserId();
        await _commitNotes.RefreshNotesForReleaseAsync(userId, releaseId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{releaseId:guid}/teams")]
    public async Task<ActionResult<ReleaseTeamDto>> AddTeam(
        Guid releaseId,
        [FromBody] AddTeamToReleaseRequest request,
        CancellationToken cancellationToken)
    {
        var releaseExists = await _db.Releases.AnyAsync(r => r.Id == releaseId, cancellationToken);
        if (!releaseExists)
            return NotFound();

        var teamExists = await _db.Teams.AnyAsync(t => t.Id == request.TeamId, cancellationToken);
        if (!teamExists)
            return BadRequest("Team was not found.");

        var existing = await _db.ReleaseTeams.FirstOrDefaultAsync(
            rt => rt.ReleaseId == releaseId && rt.TeamId == request.TeamId,
            cancellationToken);

        if (existing is not null)
        {
            var name = await _db.Teams.Where(t => t.Id == request.TeamId).Select(t => t.Name).FirstAsync(cancellationToken);
            return Ok(new ReleaseTeamDto(existing.Id, request.TeamId, name));
        }

        var rt = new ReleaseTeam
        {
            Id = Guid.NewGuid(),
            ReleaseId = releaseId,
            TeamId = request.TeamId
        };

        _db.ReleaseTeams.Add(rt);
        await _db.SaveChangesAsync(cancellationToken);

        var teamName = await _db.Teams.Where(t => t.Id == request.TeamId).Select(t => t.Name).FirstAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = releaseId }, new ReleaseTeamDto(rt.Id, request.TeamId, teamName));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var release = await _db.Releases.FindAsync([id], cancellationToken);
        if (release is null)
            return NotFound();

        _db.Releases.Remove(release);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private IReadOnlyList<CommitGroupDto>? BuildCommitGroups(
        IReadOnlyList<ReleaseCommitItemDto> commits,
        AppSettings settings)
    {
        if (!settings.ConventionalCommitsEnabled || commits.Count == 0)
            return null;

        var items = commits.Select(c =>
        {
            var parsed = _commitParser.Parse(c.Comment);

            IReadOnlyList<JiraTicketRefDto> jiraRefs = [];
            if (settings.JiraEnabled
                && !string.IsNullOrEmpty(settings.JiraProjectKey)
                && !string.IsNullOrEmpty(settings.JiraBaseUrl))
            {
                var keys = _jiraExtractor.ExtractKeys(c.Comment, settings.JiraProjectKey);
                jiraRefs = keys
                    .Select(k => new JiraTicketRefDto(k, $"{settings.JiraBaseUrl!.TrimEnd('/')}/browse/{k}"))
                    .ToList();
            }

            return (Commit: c, Parsed: parsed, JiraRefs: jiraRefs);
        });

        return _commitParser.BuildGroups(items, settings.ConventionalCommitsUseEmojis, settings.GetExcludedGroupsSet());
    }
}
