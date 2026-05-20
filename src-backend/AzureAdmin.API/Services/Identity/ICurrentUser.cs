namespace AzureAdmin.API.Services.Identity;

public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid GetRequiredUserId();
}
