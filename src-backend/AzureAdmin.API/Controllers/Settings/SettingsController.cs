using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Controllers.Settings;

[ApiController]
[Authorize]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public SettingsController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<AppSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var settings = await _db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == AppSettings.SingletonId, cancellationToken)
            ?? new AppSettings();

        return Ok(ToDto(settings));
    }

    [HttpPut]
    public async Task<ActionResult<AppSettingsDto>> Update(
        [FromBody] UpdateAppSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Id == AppSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            settings = new AppSettings { Id = AppSettings.SingletonId };
            _db.AppSettings.Add(settings);
        }

        settings.ConventionalCommitsEnabled = request.ConventionalCommitsEnabled;
        settings.ConventionalCommitsUseEmojis = request.ConventionalCommitsUseEmojis;
        settings.ExcludedGroups = request.ExcludedGroups?.Count > 0
            ? string.Join(',', request.ExcludedGroups.Select(g => g.Trim()).Where(g => !string.IsNullOrEmpty(g)))
            : null;
        settings.JiraEnabled = request.JiraEnabled;
        settings.JiraBaseUrl = string.IsNullOrWhiteSpace(request.JiraBaseUrl) ? null : request.JiraBaseUrl.Trim();
        settings.JiraProjectKey = string.IsNullOrWhiteSpace(request.JiraProjectKey)
            ? null
            : request.JiraProjectKey.Trim().ToUpperInvariant();

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(settings));
    }

    private static AppSettingsDto ToDto(AppSettings s) => new(
        s.ConventionalCommitsEnabled,
        s.ConventionalCommitsUseEmojis,
        s.GetExcludedGroupsSet().ToList(),
        s.JiraEnabled,
        s.JiraBaseUrl,
        s.JiraProjectKey);
}
