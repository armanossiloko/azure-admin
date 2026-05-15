namespace AzureAdmin.Api.Services.Identity;

public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid GetRequiredUserId();
}
