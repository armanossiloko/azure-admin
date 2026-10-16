namespace AzureAdmin.API.Contracts;

/// <summary>Resolved branding shown in the SPA (always non-empty after defaults).</summary>
public sealed record BrandingDto(
    string ApplicationName,
    string Tagline,
    string Description);
