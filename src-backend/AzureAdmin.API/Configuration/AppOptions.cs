namespace AzureAdmin.API.Configuration;

/// <summary>General application settings (<c>App</c> section).</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// Public URL of the Azure Admin UI, e.g. <c>https://azure-admin.example.com</c> (env <c>App__PublicBaseUrl</c>).
    /// Used to build release links in chat notifications. Optional: without it, links are derived from the
    /// incoming request, so messages sent by the background status sync (no request) carry no link.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}
