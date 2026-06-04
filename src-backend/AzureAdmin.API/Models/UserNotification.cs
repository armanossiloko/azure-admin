namespace AzureAdmin.API.Models;

public sealed class UserNotification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    /// <summary>Stable key for upsert (e.g. pat-expiring:{organizationId}).</summary>
    public string DedupeKey { get; set; } = "";

    public string Kind { get; set; } = "";

    public string Title { get; set; } = "";

    public string? Body { get; set; }

    public string? Href { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}
