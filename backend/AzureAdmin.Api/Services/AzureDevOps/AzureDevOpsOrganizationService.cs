using System.Text.RegularExpressions;
using AzureAdmin.Api.Contracts;
using AzureAdmin.Api.Data;
using AzureAdmin.Api.Models;
using AzureAdmin.Api.Services.Identity;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.Api.Services.AzureDevOps;

public sealed class AzureDevOpsOrganizationService
{
    private static readonly Regex OrganizationSlug = new(
        @"^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,254}[a-zA-Z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AzureDevOpsOrganizationService(ApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public static string NormalizeKey(string organization) =>
        AzureDevOpsPatCredentialService.NormalizeOrganization(organization);

    public static void ValidateOrganizationSlug(string organization, string paramName = "organization")
    {
        var trimmed = organization?.Trim() ?? "";
        if (trimmed.Length is 0 or > 256)
            throw new ArgumentException("Organization must be 1–256 characters.", paramName);

        if (trimmed.Contains('/', StringComparison.Ordinal) || trimmed.Contains('\\', StringComparison.Ordinal))
            throw new ArgumentException("Organization must not contain path separators.", paramName);

        if (trimmed.Contains(' ', StringComparison.Ordinal))
            throw new ArgumentException("Organization must not contain spaces.", paramName);

        if (!OrganizationSlug.IsMatch(trimmed))
            throw new ArgumentException(
                "Organization may only contain letters, numbers, and hyphens, and must start/end with a letter or number.",
                paramName);
    }

    public async Task<IReadOnlyList<AzureDevOpsOrganizationSummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();

        return await (
                from o in _db.UserAzureDevOpsOrganizations.AsNoTracking()
                where o.UserId == userId
                join p in _db.AzureDevOpsPatCredentials.AsNoTracking()
                    on new { o.UserId, o.OrganizationKey } equals new { p.UserId, p.OrganizationKey } into gj
                from p in gj.DefaultIfEmpty()
                orderby o.OrganizationDisplay
                select new AzureDevOpsOrganizationSummaryDto(
                    o.Id,
                    o.OrganizationKey,
                    o.OrganizationDisplay,
                    o.Notes,
                    p != null,
                    p != null ? p.Id : (Guid?)null,
                    p != null ? p.UpdatedAt : (DateTimeOffset?)null,
                    p != null ? p.PatExpiresAt : null))
            .ToListAsync(cancellationToken);
    }

    public async Task<AzureDevOpsOrganizationSummaryDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        return await (
                from o in _db.UserAzureDevOpsOrganizations.AsNoTracking()
                where o.Id == id && o.UserId == userId
                join p in _db.AzureDevOpsPatCredentials.AsNoTracking()
                    on new { o.UserId, o.OrganizationKey } equals new { p.UserId, p.OrganizationKey } into gj
                from p in gj.DefaultIfEmpty()
                select new AzureDevOpsOrganizationSummaryDto(
                    o.Id,
                    o.OrganizationKey,
                    o.OrganizationDisplay,
                    o.Notes,
                    p != null,
                    p != null ? p.Id : (Guid?)null,
                    p != null ? p.UpdatedAt : (DateTimeOffset?)null,
                    p != null ? p.PatExpiresAt : null))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Creates the org row if missing; used when saving PATs so orgs always exist in the catalog.</summary>
    public async Task<UserAzureDevOpsOrganization> EnsureOrganizationAsync(
        Guid userId,
        string organizationKey,
        string organizationDisplay,
        CancellationToken cancellationToken)
    {
        var existing = await _db.UserAzureDevOpsOrganizations.FirstOrDefaultAsync(
            o => o.UserId == userId && o.OrganizationKey == organizationKey,
            cancellationToken);
        if (existing is not null)
            return existing;

        var now = DateTimeOffset.UtcNow;
        var entity = new UserAzureDevOpsOrganization
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrganizationKey = organizationKey,
            OrganizationDisplay = organizationDisplay.Trim(),
            Notes = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.UserAzureDevOpsOrganizations.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<AzureDevOpsOrganizationSummaryDto> CreateAsync(
        CreateAzureDevOpsOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateOrganizationSlug(request.Organization, nameof(request.Organization));

        var userId = _currentUser.GetRequiredUserId();
        var key = NormalizeKey(request.Organization);
        var display = request.Organization.Trim();

        if (!string.IsNullOrWhiteSpace(request.OrganizationDisplay))
        {
            var d = request.OrganizationDisplay.Trim();
            ValidateOrganizationSlug(d, nameof(request.OrganizationDisplay));
            if (!string.Equals(NormalizeKey(d), key, StringComparison.Ordinal))
                throw new ArgumentException(
                    "OrganizationDisplay must refer to the same organization as Organization.",
                    nameof(request.OrganizationDisplay));
            display = d;
        }

        var exists = await _db.UserAzureDevOpsOrganizations.AnyAsync(
            o => o.UserId == userId && o.OrganizationKey == key,
            cancellationToken);
        if (exists)
            throw new InvalidOperationException($"Organization '{display}' is already in your list.");

        var now = DateTimeOffset.UtcNow;
        var entity = new UserAzureDevOpsOrganization
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrganizationKey = key,
            OrganizationDisplay = display,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.UserAzureDevOpsOrganizations.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return new AzureDevOpsOrganizationSummaryDto(
            entity.Id,
            entity.OrganizationKey,
            entity.OrganizationDisplay,
            entity.Notes,
            false,
            null,
            null,
            null);
    }

    public async Task<AzureDevOpsOrganizationSummaryDto?> UpdateAsync(
        Guid id,
        UpdateAzureDevOpsOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var entity = await _db.UserAzureDevOpsOrganizations.FirstOrDefaultAsync(
            o => o.Id == id && o.UserId == userId,
            cancellationToken);
        if (entity is null)
            return null;

        if (request.Notes is not null)
            entity.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        if (!string.IsNullOrWhiteSpace(request.OrganizationDisplay))
        {
            ValidateOrganizationSlug(request.OrganizationDisplay, nameof(request.OrganizationDisplay));
            if (NormalizeKey(request.OrganizationDisplay) != entity.OrganizationKey)
                throw new ArgumentException("Display name must match the same organization (casing only).", nameof(request));
            entity.OrganizationDisplay = request.OrganizationDisplay.Trim();
        }

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var entity = await _db.UserAzureDevOpsOrganizations.FirstOrDefaultAsync(
            o => o.Id == id && o.UserId == userId,
            cancellationToken);
        if (entity is null)
            return false;

        var pats = await _db.AzureDevOpsPatCredentials
            .Where(c => c.UserId == userId && c.OrganizationKey == entity.OrganizationKey)
            .ToListAsync(cancellationToken);
        _db.AzureDevOpsPatCredentials.RemoveRange(pats);

        _db.UserAzureDevOpsOrganizations.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
