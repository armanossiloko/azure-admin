using System.Security.Claims;
using AzureAdmin.API.Models;
using Microsoft.AspNetCore.Identity;

namespace AzureAdmin.API.Services.Identity;

/// <summary>
/// Maps a Keycloak login to the local <see cref="ApplicationUser"/>, creating or linking it when needed.
/// </summary>
public sealed class KeycloakUserProvisioner
{
    public const string LoginProvider = "Keycloak";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<KeycloakUserProvisioner> _logger;

    public KeycloakUserProvisioner(UserManager<ApplicationUser> userManager, ILogger<KeycloakUserProvisioner> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<ApplicationUser> GetOrCreateAsync(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue("sub")
            ?? throw new InvalidOperationException("OIDC token missing 'sub' claim.");

        var displayName = principal.FindFirstValue("name")
            ?? principal.FindFirstValue("preferred_username");

        var user = await _userManager.FindByLoginAsync(LoginProvider, sub);

        if (user is null)
        {
            var email = principal.FindFirstValue("email")
                ?? principal.FindFirstValue(ClaimTypes.Email)
                ?? sub;

            // A local user with this e-mail can already exist without a link to this Keycloak subject:
            // a first login that was interrupted after the user was created, or a subject that changed
            // (e.g. re-imported realm). Link that user instead of failing on the unique e-mail.
            user = await _userManager.FindByEmailAsync(email);

            if (user is null)
            {
                user = await CreateAsync(sub, email, displayName);
            }
            else
            {
                await LinkAsync(user, sub);
            }
        }

        // Sync the display name if it changed in Keycloak.
        if (displayName is not null && displayName != user.DisplayName)
        {
            user.DisplayName = displayName;
            await _userManager.UpdateAsync(user);
        }

        return user;
    }

    private async Task<ApplicationUser> CreateAsync(string sub, string email, string? displayName)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = sub,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
        };

        var create = await _userManager.CreateAsync(user);
        if (!create.Succeeded)
            throw new InvalidOperationException($"Failed to create user for sub '{sub}': {Describe(create)}");

        var addLogin = await _userManager.AddLoginAsync(user, new UserLoginInfo(LoginProvider, sub, LoginProvider));
        if (!addLogin.Succeeded)
        {
            // Do not leave a user behind that can never log in again.
            await _userManager.DeleteAsync(user);
            throw new InvalidOperationException($"Failed to link user to sub '{sub}': {Describe(addLogin)}");
        }

        return user;
    }

    private async Task LinkAsync(ApplicationUser user, string sub)
    {
        var existing = (await _userManager.GetLoginsAsync(user))
            .Where(l => l.LoginProvider == LoginProvider)
            .ToList();

        foreach (var old in existing)
        {
            _logger.LogWarning(
                "Keycloak subject for '{Email}' changed from '{OldSub}' to '{NewSub}'; relinking the existing user {UserId}.",
                user.Email, old.ProviderKey, sub, user.Id);

            var remove = await _userManager.RemoveLoginAsync(user, old.LoginProvider, old.ProviderKey);
            if (!remove.Succeeded)
                throw new InvalidOperationException($"Failed to unlink old Keycloak subject '{old.ProviderKey}': {Describe(remove)}");
        }

        if (existing.Count == 0)
        {
            _logger.LogWarning(
                "Existing user {UserId} ('{Email}') had no Keycloak link; linking it to subject '{NewSub}'.",
                user.Id, user.Email, sub);
        }

        var add = await _userManager.AddLoginAsync(user, new UserLoginInfo(LoginProvider, sub, LoginProvider));
        if (!add.Succeeded)
            throw new InvalidOperationException($"Failed to link user {user.Id} to sub '{sub}': {Describe(add)}");
    }

    private static string Describe(IdentityResult result) =>
        string.Join(", ", result.Errors.Select(e => e.Description));
}
