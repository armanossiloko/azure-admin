using AzureAdmin.API.Models;

namespace AzureAdmin.API.Services.Releases;

public static class ReleasePullRequestStatusMapper
{
    /// <summary>ADO returns PR status as a string (e.g. <c>active</c>) or sometimes a numeric enum value.</summary>
    public static ReleasePullRequestStatus Map(string? adoStatus)
    {
        if (string.IsNullOrWhiteSpace(adoStatus))
            return ReleasePullRequestStatus.Unknown;

        if (int.TryParse(adoStatus, out var numeric))
        {
            return numeric switch
            {
                1 => ReleasePullRequestStatus.Active,
                2 => ReleasePullRequestStatus.Abandoned,
                3 => ReleasePullRequestStatus.Completed,
                _ => ReleasePullRequestStatus.Unknown
            };
        }

        return adoStatus.Trim().ToLowerInvariant() switch
        {
            "active" => ReleasePullRequestStatus.Active,
            "completed" => ReleasePullRequestStatus.Completed,
            "abandoned" => ReleasePullRequestStatus.Abandoned,
            _ => ReleasePullRequestStatus.Unknown
        };
    }
}
