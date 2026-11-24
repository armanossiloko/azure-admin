using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Identity;
using AzureAdmin.API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Tests;

/// <summary>
/// The PAT sync runs on every notification list and navigation load. It must not undo "mark as read",
/// or an expired PAT keeps the unread dot on forever and "Mark all read" appears to do nothing.
/// </summary>
public sealed class PatExpiryNotificationReadStateTests
{
    private sealed class FixedUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public Guid GetRequiredUserId() => id;
    }

    private static DbContextOptions<ApplicationDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static async Task SeedOrganizationWithPatAsync(
        DbContextOptions<ApplicationDbContext> options, Guid userId, DateTimeOffset patExpiresAt)
    {
        await using var db = new ApplicationDbContext(options);
        db.UserAzureDevOpsOrganizations.Add(new UserAzureDevOpsOrganization
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrganizationKey = "contoso",
            OrganizationDisplay = "Contoso"
        });
        db.AzureDevOpsPatCredentials.Add(new AzureDevOpsPatCredential
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrganizationKey = "contoso",
            OrganizationDisplay = "Contoso",
            ProtectedPat = [1, 2, 3],
            PatExpiresAt = patExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SyncAsync(DbContextOptions<ApplicationDbContext> options, Guid userId)
    {
        await using var db = new ApplicationDbContext(options);
        await new NotificationService(db, new FixedUser(userId)).SyncPatExpiryNotificationsAsync(CancellationToken.None);
    }

    private static async Task MarkAllReadAsync(DbContextOptions<ApplicationDbContext> options, Guid userId)
    {
        await using var db = new ApplicationDbContext(options);
        foreach (var n in await db.UserNotifications.Where(n => n.UserId == userId).ToListAsync())
            n.ReadAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task<int> UnreadCountAsync(DbContextOptions<ApplicationDbContext> options, Guid userId)
    {
        await using var db = new ApplicationDbContext(options);
        return await new NotificationService(db, new FixedUser(userId)).GetUnreadCountAsync(CancellationToken.None);
    }

    [Fact]
    public async Task An_expired_pat_notification_stays_read_after_the_next_sync()
    {
        var options = NewDatabase();
        var userId = Guid.NewGuid();
        await SeedOrganizationWithPatAsync(options, userId, DateTimeOffset.UtcNow.AddDays(-3));

        await SyncAsync(options, userId);
        Assert.Equal(1, await UnreadCountAsync(options, userId));

        await MarkAllReadAsync(options, userId);
        await SyncAsync(options, userId);

        Assert.Equal(0, await UnreadCountAsync(options, userId));
    }

    [Fact]
    public async Task A_pat_that_moves_from_expiring_soon_to_expired_alerts_again()
    {
        var options = NewDatabase();
        var userId = Guid.NewGuid();
        await SeedOrganizationWithPatAsync(options, userId, DateTimeOffset.UtcNow.AddDays(2));

        await SyncAsync(options, userId);
        await MarkAllReadAsync(options, userId);
        Assert.Equal(0, await UnreadCountAsync(options, userId));

        await using (var db = new ApplicationDbContext(options))
        {
            var pat = await db.AzureDevOpsPatCredentials.SingleAsync();
            pat.PatExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        await SyncAsync(options, userId);

        Assert.Equal(1, await UnreadCountAsync(options, userId));
    }
}
