namespace AzureAdmin.API.Models;

public enum WebhookEventKind
{
    DevToMasterCreated = 0,
    DevToMasterMerged = 1,
    MasterToProdCreated = 2,
    MasterToProdMerged = 3
}

public static class WebhookEvents
{
    public static bool IsCreated(WebhookEventKind kind) =>
        kind is WebhookEventKind.DevToMasterCreated or WebhookEventKind.MasterToProdCreated;

    public static bool IsMerged(WebhookEventKind kind) =>
        kind is WebhookEventKind.DevToMasterMerged or WebhookEventKind.MasterToProdMerged;

    public static WebhookEventKind? From(ReleasePrPhase phase, bool merged) =>
        (phase, merged) switch
        {
            (ReleasePrPhase.DevToMaster, false) => WebhookEventKind.DevToMasterCreated,
            (ReleasePrPhase.DevToMaster, true) => WebhookEventKind.DevToMasterMerged,
            (ReleasePrPhase.MasterToProd, false) => WebhookEventKind.MasterToProdCreated,
            (ReleasePrPhase.MasterToProd, true) => WebhookEventKind.MasterToProdMerged,
            _ => null
        };
}
