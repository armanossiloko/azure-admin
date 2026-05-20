namespace AzureAdmin.API.Contracts;

public sealed record CurrentUserDto(Guid Id, string Email, string? DisplayName);
