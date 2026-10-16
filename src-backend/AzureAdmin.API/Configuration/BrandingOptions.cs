namespace AzureAdmin.API.Configuration;

/// <summary>
/// Deployment-level branding defaults (appsettings / env). Database AppSettings overrides win when set.
/// </summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    public string? ApplicationName { get; set; }
    public string? Tagline { get; set; }
    public string? Description { get; set; }
}
