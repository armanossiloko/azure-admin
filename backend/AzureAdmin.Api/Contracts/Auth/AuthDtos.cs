namespace AzureAdmin.Api.Contracts;

public sealed record LoginRequest(string Email, string Password);

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record CurrentUserDto(Guid Id, string Email, string? DisplayName);
