using AzureAdmin.API.Models;

namespace AzureAdmin.API.Contracts;

public sealed record WebhookPlaceholderDto(string Token, string Description);

public sealed record WebhookDefaultTemplatesDto(
    string Created,
    string Merged,
    string SamplePreview);

public sealed record WebhookSubscriptionDto(
    Guid Id,
    string Name,
    WebhookDestinationKind Kind,
    bool Enabled,
    string UrlPreview,
    bool NotifyOnDevToMasterCreated,
    bool NotifyOnDevToMasterMerged,
    bool NotifyOnMasterToProdCreated,
    bool NotifyOnMasterToProdMerged,
    string? TemplateCreated,
    string? TemplateMerged,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastDeliveredAt,
    string? LastDeliveryError);

public sealed record WebhookSettingsDto(
    IReadOnlyList<WebhookSubscriptionDto> Destinations,
    WebhookDefaultTemplatesDto Defaults,
    IReadOnlyList<WebhookPlaceholderDto> Placeholders);

public sealed record CreateWebhookSubscriptionRequest(
    string Name,
    WebhookDestinationKind Kind,
    string Url,
    bool Enabled = true,
    bool NotifyOnDevToMasterCreated = true,
    bool NotifyOnDevToMasterMerged = true,
    bool NotifyOnMasterToProdCreated = true,
    bool NotifyOnMasterToProdMerged = true,
    string? TemplateCreated = null,
    string? TemplateMerged = null);

/// <param name="Url">When null or blank, the stored URL is left unchanged.</param>
public sealed record UpdateWebhookSubscriptionRequest(
    string Name,
    bool Enabled,
    bool NotifyOnDevToMasterCreated,
    bool NotifyOnDevToMasterMerged,
    bool NotifyOnMasterToProdCreated,
    bool NotifyOnMasterToProdMerged,
    string? Url = null,
    string? TemplateCreated = null,
    string? TemplateMerged = null);

public sealed record WebhookTestResultDto(bool Ok, string? Error);
