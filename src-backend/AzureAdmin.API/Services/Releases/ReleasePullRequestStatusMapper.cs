using AzureAdmin.API.Models;

namespace AzureAdmin.API.Services.Releases;

public static class ReleasePullRequestStatusMapper
{
    /// <summary>
    /// True when a status lookup returned a real Azure DevOps value we should persist.
    /// 404 (null/empty) and transport failures must not overwrite the stored row: the background
    /// sync may use another user's PAT, and a PAT that cannot see the PR would otherwise mark
    /// existing Active rows as <see cref="ReleasePullRequestStatus.Unknown"/>.
    /// </summary>
    public static bool CanPersistRemoteStatus(bool lookupSucceeded, string? adoStatus) =>
        lookupSucceeded && !string.IsNullOrWhiteSpace(adoStatus);

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
