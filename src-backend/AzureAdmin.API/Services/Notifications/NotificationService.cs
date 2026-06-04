using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Identity;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Services.Notifications;

public sealed class NotificationService
{
    private static readonly TimeSpan PatExpiringSoonWindow = TimeSpan.FromDays(14);

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public NotificationService(ApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task SyncPatExpiryNotificationsAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();

        var prefs = await _db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (prefs is { NotifyPatExpiry: false })
            return;

        var patRows = await (
                from p in _db.AzureDevOpsPatCredentials.AsNoTracking()
                where p.UserId == userId && p.PatExpiresAt != null
                join o in _db.UserAzureDevOpsOrganizations.AsNoTracking()
                    on new { p.UserId, p.OrganizationKey } equals new { o.UserId, o.OrganizationKey }
                select new
                {
                    OrgId = o.Id,
                    o.OrganizationDisplay,
                    p.PatExpiresAt
                })
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var activeKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in patRows)
        {
            var expires = row.PatExpiresAt!.Value;
            string kind;
            string title;
            string body;

            if (expires <= now)
            {
                kind = "pat_expired";
                title = $"PAT expired — {row.OrganizationDisplay}";
                body = $"Your Azure DevOps PAT for {row.OrganizationDisplay} has expired. Update it to restore catalog import and PR batching.";
            }
            else if (expires <= now + PatExpiringSoonWindow)
            {
                kind = "pat_expiring_soon";
                title = $"PAT expiring soon — {row.OrganizationDisplay}";
                body = $"Your PAT for {row.OrganizationDisplay} expires on {expires:yyyy-MM-dd}. Renew it in Azure DevOps and update the expiration date here.";
            }
            else
            {
                continue;
            }

            var dedupeKey = $"{kind}:{row.OrgId}";
            activeKeys.Add(dedupeKey);

            var existing = await _db.UserNotifications
                .FirstOrDefaultAsync(n => n.UserId == userId && n.DedupeKey == dedupeKey, cancellationToken);

            if (existing is null)
            {
                _db.UserNotifications.Add(new UserNotification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    DedupeKey = dedupeKey,
                    Kind = kind,
                    Title = title,
                    Body = body,
                    Href = $"/settings/azure-organizations/{row.OrgId}",
                    CreatedAt = now
                });
            }
            else
            {
                existing.Kind = kind;
                existing.Title = title;
                existing.Body = body;
                existing.Href = $"/settings/azure-organizations/{row.OrgId}";
                if (existing.ReadAt is not null && kind == "pat_expired")
                    existing.ReadAt = null;
            }
        }

        var stalePatKeys = await _db.UserNotifications
            .Where(n => n.UserId == userId && (n.Kind == "pat_expired" || n.Kind == "pat_expiring_soon"))
            .Select(n => n.DedupeKey)
            .ToListAsync(cancellationToken);

        foreach (var key in stalePatKeys)
        {
            if (!activeKeys.Contains(key))
            {
                var row = await _db.UserNotifications
                    .FirstOrDefaultAsync(n => n.UserId == userId && n.DedupeKey == key, cancellationToken);
                if (row is not null)
                    _db.UserNotifications.Remove(row);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        return await _db.UserNotifications.AsNoTracking()
            .CountAsync(n => n.UserId == userId && n.ReadAt == null, cancellationToken);
    }
}
