using AzureAdmin.API.Contracts.Account;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Controllers.Account;

[ApiController]
[Authorize]
[Route("api/account")]
public sealed class AccountSettingsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountSettingsController(
        ApplicationDbContext db,
        ICurrentUser currentUser,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _currentUser = currentUser;
        _userManager = userManager;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<AccountSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Unauthorized();

        var prefs = await _db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        return Ok(new AccountSettingsDto(
            userId,
            user.Email ?? "",
            user.DisplayName,
            prefs?.DefaultOrganizationId,
            prefs?.PreferredTheme,
            prefs?.NotifyPatExpiry ?? true));
    }

    [HttpPatch("settings")]
    public async Task<ActionResult<AccountSettingsDto>> UpdateSettings(
        [FromBody] UpdateAccountSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return Unauthorized();

        if (request.UpdateDefaultOrganization && request.DefaultOrganizationId is { } orgId)
        {
            var orgExists = await _db.UserAzureDevOpsOrganizations.AsNoTracking()
                .AnyAsync(o => o.Id == orgId && o.UserId == userId, cancellationToken);
            if (!orgExists)
                return BadRequest("Organization was not found.");
        }

        if (request.PreferredTheme is not null &&
            request.PreferredTheme is not ("light" or "dark"))
            return BadRequest("PreferredTheme must be 'light' or 'dark'.");

        var prefs = await _db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (prefs is null)
        {
            prefs = new UserPreferences { UserId = userId };
            _db.UserPreferences.Add(prefs);
        }

        if (request.UpdateDefaultOrganization)
            prefs.DefaultOrganizationId = request.DefaultOrganizationId;

        if (request.PreferredTheme is not null)
            prefs.PreferredTheme = request.PreferredTheme;

        if (request.NotifyPatExpiry is { } notify)
            prefs.NotifyPatExpiry = notify;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new AccountSettingsDto(
            userId,
            user.Email ?? "",
            user.DisplayName,
            prefs.DefaultOrganizationId,
            prefs.PreferredTheme,
            prefs.NotifyPatExpiry));
    }
}
