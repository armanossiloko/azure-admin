namespace AzureAdmin.API.Models;

/// <summary>
/// Outbound incoming-webhook destination (Teams, Slack, or Discord). The URL is encrypted at rest.
/// </summary>
public sealed class WebhookSubscription
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public WebhookDestinationKind Kind { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Data-protection encrypted incoming-webhook URL.</summary>
    public byte[] ProtectedUrl { get; set; } = [];

    /// <summary>Masked URL for display; never the full secret.</summary>
    public string UrlPreview { get; set; } = "";

    public bool NotifyOnDevToMasterCreated { get; set; } = true;
    public bool NotifyOnDevToMasterMerged { get; set; } = true;
    public bool NotifyOnMasterToProdCreated { get; set; } = true;
    public bool NotifyOnMasterToProdMerged { get; set; } = true;

    /// <summary>Null or whitespace means the built-in created template is used.</summary>
    public string? TemplateCreated { get; set; }

    /// <summary>Null or whitespace means the built-in merged template is used.</summary>
    public string? TemplateMerged { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastDeliveredAt { get; set; }
    public string? LastDeliveryError { get; set; }
}
