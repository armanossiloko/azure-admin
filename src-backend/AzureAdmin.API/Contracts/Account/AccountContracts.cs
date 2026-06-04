namespace AzureAdmin.API.Contracts.Account;

public sealed record AccountSettingsDto(
    Guid UserId,
    string Email,
    string? DisplayName,
    Guid? DefaultOrganizationId,
    string? PreferredTheme,
    bool NotifyPatExpiry);

public sealed record UpdateAccountSettingsRequest(
    bool UpdateDefaultOrganization = false,
    Guid? DefaultOrganizationId = null,
    string? PreferredTheme = null,
    bool? NotifyPatExpiry = null);
