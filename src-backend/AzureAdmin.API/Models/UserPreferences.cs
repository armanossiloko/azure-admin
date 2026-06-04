namespace AzureAdmin.API.Models;

public sealed class UserPreferences
{
    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public Guid? DefaultOrganizationId { get; set; }

    /// <summary>light or dark; null uses client default.</summary>
    public string? PreferredTheme { get; set; }

    public bool NotifyPatExpiry { get; set; } = true;
}
