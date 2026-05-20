using System.Security.Claims;
using System.Text.Json.Serialization;
using AzureAdmin.Api.Configuration;
using AzureAdmin.Api.Data;
using AzureAdmin.Api.DependencyInjection;
using AzureAdmin.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var pg = builder.Configuration
        .GetSection(PostgresOptions.SectionName)
        .Get<PostgresOptions>()
        ?? throw new InvalidOperationException($"Missing '{PostgresOptions.SectionName}' configuration section.");

    options.UseNpgsql(pg.ToConnectionString());
});

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<ApplicationDbContext>()
    .SetApplicationName("AzureAdmin.Api");

// Identity Core — user store only; no password-based sign-in, no auth schemes.
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Read Keycloak options once for auth configuration.
var keycloak = builder.Configuration
    .GetSection(KeycloakOptions.SectionName)
    .Get<KeycloakOptions>()
    ?? throw new InvalidOperationException($"Missing '{KeycloakOptions.SectionName}' configuration section.");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "AzureAdmin.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Authority = keycloak.Authority;
        options.ClientId = keycloak.ClientId;
        options.ClientSecret = keycloak.ClientSecret;
        options.ResponseType = "code"; // PKCE authorization code flow
        options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
        options.CallbackPath = keycloak.CallbackPath;
        options.SignedOutCallbackPath = keycloak.SignedOutCallbackPath;
        options.MapInboundClaims = false; // Keep OIDC claim names as-is (sub, email, name, ...)
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        options.Events.OnTokenValidated = async ctx =>
        {
            var principal = ctx.Principal
                ?? throw new InvalidOperationException("OIDC token validated but Principal is null.");

            var sub = principal.FindFirstValue("sub")
                ?? throw new InvalidOperationException("OIDC token missing 'sub' claim.");

            var userManager = ctx.HttpContext.RequestServices
                .GetRequiredService<UserManager<ApplicationUser>>();

            // Find existing local user by the Keycloak subject.
            var user = await userManager.FindByLoginAsync("Keycloak", sub);

            if (user is null)
            {
                // First login — provision a local ApplicationUser.
                var email = principal.FindFirstValue("email")
                    ?? principal.FindFirstValue(ClaimTypes.Email)
                    ?? sub;

                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = sub,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = principal.FindFirstValue("name")
                        ?? principal.FindFirstValue("preferred_username"),
                };

                var create = await userManager.CreateAsync(user);
                if (!create.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to create user for sub '{sub}': {string.Join(", ", create.Errors.Select(e => e.Description))}");

                await userManager.AddLoginAsync(user, new UserLoginInfo("Keycloak", sub, "Keycloak"));
            }
            else
            {
                // Subsequent login — sync display name if it changed in Keycloak.
                var freshName = principal.FindFirstValue("name")
                    ?? principal.FindFirstValue("preferred_username");

                if (freshName is not null && freshName != user.DisplayName)
                {
                    user.DisplayName = freshName;
                    await userManager.UpdateAsync(user);
                }
            }

            // Replace the principal with a minimal, stable set of local claims.
            // The cookie stores only these; no Keycloak tokens are persisted.
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email ?? ""),
                new Claim("displayName", user.DisplayName ?? ""),
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            ctx.Principal = new ClaimsPrincipal(identity);
            ctx.Properties!.IsPersistent = true;
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddApplicationServices(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Avoid redirecting proxied HTTP to HTTPS; static file serving is same-origin in production.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Serve the Angular PWA build from wwwroot/.
app.UseStaticFiles();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// All non-API routes fall back to index.html so Angular handles client-side routing.
app.MapFallbackToFile("index.html");

app.Run();
