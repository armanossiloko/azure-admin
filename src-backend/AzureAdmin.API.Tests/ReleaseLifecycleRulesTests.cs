using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Releases;

namespace AzureAdmin.API.Tests;

public sealed class ReleaseLifecycleRulesTests
{
    [Theory]
    [InlineData(ReleaseLifecycleStatus.Draft, ReleaseLifecycleStatus.Active, true)]
    [InlineData(ReleaseLifecycleStatus.Draft, ReleaseLifecycleStatus.Completed, true)]
    [InlineData(ReleaseLifecycleStatus.Draft, ReleaseLifecycleStatus.Archived, true)]
    [InlineData(ReleaseLifecycleStatus.Active, ReleaseLifecycleStatus.Completed, true)]
    [InlineData(ReleaseLifecycleStatus.Active, ReleaseLifecycleStatus.Archived, true)]
    [InlineData(ReleaseLifecycleStatus.Completed, ReleaseLifecycleStatus.Archived, true)]
    [InlineData(ReleaseLifecycleStatus.Active, ReleaseLifecycleStatus.Draft, false)]
    [InlineData(ReleaseLifecycleStatus.Completed, ReleaseLifecycleStatus.Active, false)]
    [InlineData(ReleaseLifecycleStatus.Archived, ReleaseLifecycleStatus.Draft, false)]
    [InlineData(ReleaseLifecycleStatus.Archived, ReleaseLifecycleStatus.Active, false)]
    public void IsAllowedStatusTransition(ReleaseLifecycleStatus from, ReleaseLifecycleStatus to, bool expected)
    {
        Assert.Equal(expected, ReleaseLifecycleRules.IsAllowedStatusTransition(from, to));
    }

    [Fact]
    public void SameStatusIsAllowed()
    {
        Assert.True(ReleaseLifecycleRules.IsAllowedStatusTransition(
            ReleaseLifecycleStatus.Active,
            ReleaseLifecycleStatus.Active));
    }
}
