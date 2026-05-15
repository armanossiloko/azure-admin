using Microsoft.AspNetCore.Identity;

namespace AzureAdmin.Api.Models;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
}
