namespace AzureAdmin.Api.Contracts;

public sealed record CurrentUserDto(Guid Id, string Email, string? DisplayName);
