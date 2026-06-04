using AzureAdmin.API.Contracts.Notifications;
using AzureAdmin.API.Data;
using AzureAdmin.API.Services.Identity;
using AzureAdmin.API.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Controllers.Notifications;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly NotificationService _notifications;

    public NotificationsController(
        ApplicationDbContext db,
        ICurrentUser currentUser,
        NotificationService notifications)
    {
        _db = db;
        _currentUser = currentUser;
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List(CancellationToken cancellationToken)
    {
        await _notifications.SyncPatExpiryNotificationsAsync(cancellationToken);

        var userId = _currentUser.GetRequiredUserId();
        var rows = await _db.UserNotifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new NotificationDto(
                n.Id,
                n.Kind,
                n.Title,
                n.Body,
                n.Href,
                n.CreatedAt,
                n.ReadAt != null))
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var row = await _db.UserNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);
        if (row is null)
            return NotFound();

        row.ReadAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var now = DateTimeOffset.UtcNow;
        await _db.UserNotifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);
        return NoContent();
    }
}
