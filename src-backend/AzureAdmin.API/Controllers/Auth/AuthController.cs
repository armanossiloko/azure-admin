using System.Security.Claims;
using AzureAdmin.API.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AzureAdmin.API.Controllers.Auth;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    /// <summary>
    /// Initiates the Keycloak OIDC authorization code flow.
    /// After a successful login the browser is redirected to <paramref name="returnUrl"/>.
    /// </summary>
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = SafeReturnUrl(returnUrl),
            IsPersistent = true,
        };
        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>Signs the user out of the local session and triggers a Keycloak logout.</summary>
    [HttpGet("logout")]
    public IActionResult Logout()
    {
        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>Returns the current authenticated user's profile, derived from the session cookie.</summary>
    [HttpGet("me")]
    [Authorize]
    public ActionResult<CurrentUserDto> Me()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null || !Guid.TryParse(id, out var userId))
            return Unauthorized();

        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var displayName = User.FindFirstValue("displayName");

        return Ok(new CurrentUserDto(userId, email, displayName));
    }

    /// <summary>Ensures return URLs are relative same-origin paths to prevent open-redirect attacks.</summary>
    private static string SafeReturnUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "/";
        var url = raw.Trim();
        // Reject "//host" and "/\host" — browsers treat both as protocol-relative URLs.
        return url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\") ? url : "/";
    }
}

