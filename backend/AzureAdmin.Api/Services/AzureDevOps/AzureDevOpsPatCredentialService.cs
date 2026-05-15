using System.Security.Cryptography;
using System.Text;
using AzureAdmin.Api.Contracts;
using AzureAdmin.Api.Data;
using AzureAdmin.Api.Models;
using AzureAdmin.Api.Services.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.Api.Services.AzureDevOps;

public sealed class AzureDevOpsPatCredentialService
{
    private readonly ApplicationDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ICurrentUser _currentUser;
    private readonly AzureDevOpsOrganizationService _organizations;

    public AzureDevOpsPatCredentialService(
        ApplicationDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        ICurrentUser currentUser,
        AzureDevOpsOrganizationService organizations)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("AzureDevOps.PAT.v1");
        _currentUser = currentUser;
        _organizations = organizations;
    }

    public static string NormalizeOrganization(string organization)
    {
        if (string.IsNullOrWhiteSpace(organization))
            throw new ArgumentException("Organization is required.", nameof(organization));
        return organization.Trim().ToLowerInvariant();
    }

    public static void ValidatePatExpiresAt(DateTimeOffset patExpiresAt, string paramName = "patExpiresAt")
    {
        if (patExpiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("PAT expiration must be in the future.", paramName);
    }

    public async Task<string?> TryGetDecryptedPatAsync(Guid userId, string organization, CancellationToken cancellationToken)
    {
        var key = NormalizeOrganization(organization);
        var row = await _db.AzureDevOpsPatCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.OrganizationKey == key, cancellationToken);

        if (row is null || row.ProtectedPat.Length == 0)
            return null;

        if (row.PatExpiresAt.HasValue && row.PatExpiresAt.Value <= DateTimeOffset.UtcNow)
            return null;

        return UnprotectPat(row.ProtectedPat);
    }

    /// <summary>Creates or replaces the single PAT for the given registered organization.</summary>
    public async Task UpsertPatForOrganizationAsync(
        Guid organizationId,
        UpsertOrganizationPatCredentialRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Pat))
            throw new ArgumentException("PAT is required.", nameof(request.Pat));
        ValidatePatExpiresAt(request.PatExpiresAt, nameof(request.PatExpiresAt));

        var userId = _currentUser.GetRequiredUserId();
        var org = await _db.UserAzureDevOpsOrganizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.UserId == userId, cancellationToken);
        if (org is null)
            throw new ArgumentException("Organization was not found.", nameof(organizationId));

        await _organizations.EnsureOrganizationAsync(userId, org.OrganizationKey, org.OrganizationDisplay, cancellationToken);

        var existing = await _db.AzureDevOpsPatCredentials.FirstOrDefaultAsync(
            c => c.UserId == userId && c.OrganizationKey == org.OrganizationKey,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            var entity = new AzureDevOpsPatCredential
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OrganizationKey = org.OrganizationKey,
                OrganizationDisplay = org.OrganizationDisplay,
                DisplayName = null,
                ProtectedPat = ProtectPat(request.Pat.Trim()),
                PatExpiresAt = request.PatExpiresAt,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.AzureDevOpsPatCredentials.Add(entity);
        }
        else
        {
            existing.ProtectedPat = ProtectPat(request.Pat.Trim());
            existing.PatExpiresAt = request.PatExpiresAt;
            existing.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeletePatForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var org = await _db.UserAzureDevOpsOrganizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.UserId == userId, cancellationToken);
        if (org is null)
            return false;

        var entity = await _db.AzureDevOpsPatCredentials.FirstOrDefaultAsync(
            c => c.UserId == userId && c.OrganizationKey == org.OrganizationKey,
            cancellationToken);
        if (entity is null)
            return false;

        _db.AzureDevOpsPatCredentials.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private byte[] ProtectPat(string plainPat) => _protector.Protect(Encoding.UTF8.GetBytes(plainPat));

    private string UnprotectPat(byte[] protectedBytes)
    {
        try
        {
            var bytes = _protector.Unprotect(protectedBytes);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Stored PAT could not be decrypted (data protection keys may have changed). Re-save the PAT in the admin UI.",
                ex);
        }
    }
}
