using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace AzureAdmin.API.Services.Identity;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var id = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(id, out var guid))
                return guid;

            // No HTTP request (background sync): fall back to the impersonated user, if any.
            return AmbientUser.UserId;
        }
    }

    public Guid GetRequiredUserId() =>
        UserId ?? throw new InvalidOperationException("The current user is not authenticated.");
}
