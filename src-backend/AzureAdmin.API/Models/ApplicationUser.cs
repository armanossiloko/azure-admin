using Microsoft.AspNetCore.Identity;

namespace AzureAdmin.API.Models;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
}
