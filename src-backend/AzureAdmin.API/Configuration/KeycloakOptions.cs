namespace AzureAdmin.API.Configuration;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Full realm URL, e.g. https://auth.example.com/realms/my-realm</summary>
    public required string Authority { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    /// <summary>Set to false in development when Keycloak runs on plain HTTP.</summary>
    public bool RequireHttpsMetadata { get; init; } = true;

    /// <summary>Path the OIDC middleware listens on for the authorization callback.</summary>
    public string CallbackPath { get; init; } = "/signin-oidc";

    /// <summary>Path the OIDC middleware listens on after Keycloak completes logout.</summary>
    public string SignedOutCallbackPath { get; init; } = "/signout-callback-oidc";
}
