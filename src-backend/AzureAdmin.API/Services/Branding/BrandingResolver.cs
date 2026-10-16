using AzureAdmin.API.Configuration;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AzureAdmin.API.Services.Branding;

public interface IBrandingResolver
{
    Task<BrandingDto> ResolveAsync(CancellationToken cancellationToken = default);
}

public sealed class BrandingResolver : IBrandingResolver
{
    private readonly ApplicationDbContext _db;
    private readonly BrandingOptions _options;

    public BrandingResolver(ApplicationDbContext db, IOptions<BrandingOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<BrandingDto> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == AppSettings.SingletonId, cancellationToken);

        return Resolve(settings, _options);
    }

    public static BrandingDto Resolve(AppSettings? settings, BrandingOptions? options) =>
        new(
            FirstNonEmpty(
                settings?.BrandingApplicationName,
                options?.ApplicationName,
                BrandingDefaults.ApplicationName),
            FirstNonEmpty(
                settings?.BrandingTagline,
                options?.Tagline,
                BrandingDefaults.Tagline),
            FirstNonEmpty(
                settings?.BrandingDescription,
                options?.Description,
                BrandingDefaults.Description));

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }
}
