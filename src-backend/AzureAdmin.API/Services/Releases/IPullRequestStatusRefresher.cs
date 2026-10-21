using AzureAdmin.API.Contracts;
using AzureAdmin.API.Models;

namespace AzureAdmin.API.Services.Releases;

/// <summary>
/// Status lookup used by interactive "Check status" and by the background sync. The hosted service
/// resolves this (not the batch service type) so a test can prove one failed release does not abort
/// the rest of the tick.
/// </summary>
internal interface IPullRequestStatusRefresher
{
    Task<IReadOnlyList<ReleasePullRequestStatusResult>> RefreshPullRequestStatusesAsync(
        Guid releaseId,
        ReleasePrPhase? phase,
        bool onlyActive,
        Func<ReleasePullRequest, Guid?> userFor,
        Guid? fallbackNotificationRecipient,
        int maxConcurrency,
        CancellationToken cancellationToken);
}
