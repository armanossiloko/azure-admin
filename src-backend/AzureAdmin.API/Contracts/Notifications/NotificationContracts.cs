namespace AzureAdmin.API.Contracts.Notifications;

public sealed record NotificationDto(
    Guid Id,
    string Kind,
    string Title,
    string? Body,
    string? Href,
    DateTimeOffset CreatedAt,
    bool IsRead);

public sealed record MarkNotificationReadRequest(bool Read = true);
