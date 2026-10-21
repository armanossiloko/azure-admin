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
            return Guid.TryParse(id, out var guid) ? guid : null;
        }
    }

    public Guid GetRequiredUserId() =>
        UserId ?? throw new InvalidOperationException("The current user is not authenticated.");
}
