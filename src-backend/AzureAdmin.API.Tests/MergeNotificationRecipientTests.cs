using AzureAdmin.API.Services.Releases;

namespace AzureAdmin.API.Tests;

public sealed class MergeNotificationRecipientTests
{
    private static readonly Guid PrCreator = Guid.NewGuid();
    private static readonly Guid ReleaseCreator = Guid.NewGuid();
    private static readonly Guid Caller = Guid.NewGuid();

    [Fact]
    public void The_user_who_opened_the_pull_request_is_notified_first()
    {
        Assert.Equal(PrCreator, ReleasePullRequestBatchService.MergeNotificationRecipient(PrCreator, ReleaseCreator, Caller));
    }

    [Fact]
    public void Legacy_pull_requests_notify_the_release_creator()
    {
        Assert.Equal(ReleaseCreator, ReleasePullRequestBatchService.MergeNotificationRecipient(null, ReleaseCreator, Caller));
    }

    [Fact]
    public void Interactive_refresh_falls_back_to_the_caller()
    {
        Assert.Equal(Caller, ReleasePullRequestBatchService.MergeNotificationRecipient(null, null, Caller));
    }

    [Fact]
    public void Background_sync_without_any_creator_notifies_nobody()
    {
        // The PAT donor is never a candidate: background sync passes no fallback.
        Assert.Null(ReleasePullRequestBatchService.MergeNotificationRecipient(null, null, fallback: null));
    }
}
