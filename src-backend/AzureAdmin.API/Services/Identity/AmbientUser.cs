namespace AzureAdmin.API.Services.Identity;

/// <summary>
/// Lets code that runs outside an HTTP request (background services) act as a specific user, so the
/// scoped services that read <see cref="ICurrentUser"/> keep working unchanged. Flows with the async
/// context; dispose the scope to clear it.
/// </summary>
public static class AmbientUser
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static Guid? UserId => Current.Value;

    public static IDisposable Impersonate(Guid userId)
    {
        var previous = Current.Value;
        Current.Value = userId;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Guid? _previous;

        public Scope(Guid? previous)
        {
            _previous = previous;
        }

        public void Dispose() => Current.Value = _previous;
    }
}
