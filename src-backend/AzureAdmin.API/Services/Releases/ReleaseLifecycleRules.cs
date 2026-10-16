using AzureAdmin.API.Models;

namespace AzureAdmin.API.Services.Releases;

public static class ReleaseLifecycleRules
{
    public static bool IsAllowedStatusTransition(ReleaseLifecycleStatus from, ReleaseLifecycleStatus to) =>
        (from, to) switch
        {
            (ReleaseLifecycleStatus.Draft, ReleaseLifecycleStatus.Active) => true,
            (ReleaseLifecycleStatus.Draft, ReleaseLifecycleStatus.Completed) => true,
            (ReleaseLifecycleStatus.Draft, ReleaseLifecycleStatus.Archived) => true,
            (ReleaseLifecycleStatus.Active, ReleaseLifecycleStatus.Completed) => true,
            (ReleaseLifecycleStatus.Active, ReleaseLifecycleStatus.Archived) => true,
            (ReleaseLifecycleStatus.Completed, ReleaseLifecycleStatus.Archived) => true,
            _ when from == to => true,
            _ => false
        };
}
