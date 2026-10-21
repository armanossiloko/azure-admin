using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Releases;

namespace AzureAdmin.API.Tests;

public sealed class ReleasePullRequestStatusMapperTests
{
    [Theory]
    [InlineData(null, ReleasePullRequestStatus.Unknown)]
    [InlineData("", ReleasePullRequestStatus.Unknown)]
    [InlineData("active", ReleasePullRequestStatus.Active)]
    [InlineData("Active", ReleasePullRequestStatus.Active)]
    [InlineData("completed", ReleasePullRequestStatus.Completed)]
    [InlineData("abandoned", ReleasePullRequestStatus.Abandoned)]
    [InlineData("1", ReleasePullRequestStatus.Active)]
    [InlineData("2", ReleasePullRequestStatus.Abandoned)]
    [InlineData("3", ReleasePullRequestStatus.Completed)]
    [InlineData("0", ReleasePullRequestStatus.Unknown)]
    [InlineData("weird", ReleasePullRequestStatus.Unknown)]
    public void Map(string? adoStatus, ReleasePullRequestStatus expected)
    {
        Assert.Equal(expected, ReleasePullRequestStatusMapper.Map(adoStatus));
    }

    [Theory]
    [InlineData(false, "active", false)]
    [InlineData(true, null, false)]
    [InlineData(true, "", false)]
    [InlineData(true, "  ", false)]
    [InlineData(true, "active", true)]
    [InlineData(true, "completed", true)]
    public void A_missing_or_failed_lookup_does_not_replace_the_stored_status(
        bool lookupSucceeded, string? adoStatus, bool persist)
    {
        Assert.Equal(persist, ReleasePullRequestStatusMapper.CanPersistRemoteStatus(lookupSucceeded, adoStatus));
    }
}
