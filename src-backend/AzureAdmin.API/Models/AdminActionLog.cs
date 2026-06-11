namespace AzureAdmin.API.Models;

/// <summary>Persistent audit trail for admin actions (e.g. branch deletions).</summary>
public sealed class AdminActionLog
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>Action identifier, e.g. <c>branch.delete</c>.</summary>
    public string Action { get; set; } = "";

    /// <summary>Target kind, e.g. <c>git.branch</c>.</summary>
    public string TargetType { get; set; } = "";

    /// <summary>Human-readable target key, e.g. <c>org/project/repo:feature/foo</c>.</summary>
    public string TargetKey { get; set; } = "";

    /// <summary>Optional JSON payload with extra context.</summary>
    public string? DetailsJson { get; set; }

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
