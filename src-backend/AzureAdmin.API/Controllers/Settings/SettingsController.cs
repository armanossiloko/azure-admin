using AzureAdmin.API.Configuration;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Branding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AzureAdmin.API.Controllers.Settings;

[ApiController]
[Authorize]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private const int MaxSprintWeeks = 52;
    private const int MaxBrandingName = 128;
    private const int MaxBrandingTagline = 256;
    private const int MaxBrandingDescription = 1024;

    private readonly ApplicationDbContext _db;
    private readonly BrandingOptions _brandingOptions;

    public SettingsController(ApplicationDbContext db, IOptions<BrandingOptions> brandingOptions)
    {
        _db = db;
        _brandingOptions = brandingOptions.Value;
    }

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
        if (request.SprintWeeks is < 0 or > MaxSprintWeeks)
            return BadRequest(new { message = $"Sprint size must be between 0 and {MaxSprintWeeks} weeks." });

        if (request.BrandingApplicationName is { Length: > MaxBrandingName })
            return BadRequest(new { message = $"Application name must be at most {MaxBrandingName} characters." });
        if (request.BrandingTagline is { Length: > MaxBrandingTagline })
            return BadRequest(new { message = $"Tagline must be at most {MaxBrandingTagline} characters." });
        if (request.BrandingDescription is { Length: > MaxBrandingDescription })
            return BadRequest(new { message = $"Description must be at most {MaxBrandingDescription} characters." });

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
        if (request.SprintWeeks is { } sprintWeeks)
            settings.SprintWeeks = sprintWeeks;
        settings.JiraEnabled = request.JiraEnabled;
        settings.JiraBaseUrl = string.IsNullOrWhiteSpace(request.JiraBaseUrl) ? null : request.JiraBaseUrl.Trim();

        // Prefer the new list; fall back to the legacy single string; leave unchanged if neither was sent
        // so an older client / partial payload cannot wipe an existing key.
        if (request.JiraProjectKeys is not null)
            settings.JiraProjectKey = SerializeProjectKeys(request.JiraProjectKeys);
        else if (request.JiraProjectKey is not null)
            settings.JiraProjectKey = SerializeProjectKeys(ParseProjectKeys(request.JiraProjectKey));

        // Null = leave unchanged (older clients). Empty/whitespace = clear override (use config/defaults).
        if (request.BrandingApplicationName is not null)
            settings.BrandingApplicationName = NormalizeOverride(request.BrandingApplicationName);
        if (request.BrandingTagline is not null)
            settings.BrandingTagline = NormalizeOverride(request.BrandingTagline);
        if (request.BrandingDescription is not null)
            settings.BrandingDescription = NormalizeOverride(request.BrandingDescription);

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(settings));
    }

    private AppSettingsDto ToDto(AppSettings s)
    {
        var keys = s.GetJiraProjectKeys();
        return new(
            s.ConventionalCommitsEnabled,
            s.ConventionalCommitsUseEmojis,
            s.GetExcludedGroupsSet().ToList(),
            s.SprintWeeks,
            s.JiraEnabled,
            s.JiraBaseUrl,
            keys,
            s.JiraProjectKey,
            s.BrandingApplicationName,
            s.BrandingTagline,
            s.BrandingDescription,
            BrandingResolver.Resolve(s, _brandingOptions));
    }

    private static string? NormalizeOverride(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> ParseProjectKeys(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',')
                .Select(k => k.Trim().ToUpperInvariant())
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct()
                .ToList();

    private static string? SerializeProjectKeys(IEnumerable<string> keys)
    {
        var normalized = keys
            .Select(k => k.Trim().ToUpperInvariant())
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct()
            .ToList();

        return normalized.Count > 0 ? string.Join(',', normalized) : null;
    }
}
