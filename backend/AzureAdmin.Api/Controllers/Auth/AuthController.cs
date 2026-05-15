using AzureAdmin.Api.Contracts;
using AzureAdmin.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AzureAdmin.Api.Controllers.Auth;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<CurrentUserDto>> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Email and password are required." });

        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim()
        };

        var create = await _userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
            return BadRequest(new { message = string.Join(" ", create.Errors.Select(e => e.Description)) });

        await _signInManager.SignInAsync(user, isPersistent: true);

        return Ok(new CurrentUserDto(user.Id, user.Email ?? email, user.DisplayName));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<CurrentUserDto>> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Email and password are required." });

        var email = request.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
            return Unauthorized(new { message = "Invalid email or password." });

        var result = await _signInManager.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized(new { message = "Invalid email or password." });

        return Ok(new CurrentUserDto(user.Id, user.Email ?? email, user.DisplayName));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserDto>> Me()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        return Ok(new CurrentUserDto(user.Id, user.Email ?? user.UserName ?? "", user.DisplayName));
    }
}
