using AzureAdmin.API.Contracts.Git;
using AzureAdmin.API.Services.Git;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AzureAdmin.API.Controllers.Admin;

[ApiController]
[Authorize]
[Route("api/admin/action-log")]
public sealed class AdminActionLogController : ControllerBase
{
    private readonly StaleBranchService _branches;

    public AdminActionLogController(StaleBranchService branches)
    {
        _branches = branches;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminActionLogDto>>> List(
        [FromQuery] string? action,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _branches.ListActionLogsAsync(action, limit, cancellationToken));
    }
}
