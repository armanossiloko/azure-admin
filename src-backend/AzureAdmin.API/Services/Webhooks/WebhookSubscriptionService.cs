using System.Security.Cryptography;
using System.Text;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Services.Webhooks;

public sealed class WebhookSubscriptionService
{
    public const int MaxNameLength = 128;
    public const int MaxTemplateLength = 4000;
    public const int MaxDestinations = 20;

    private readonly ApplicationDbContext _db;
    private readonly IDataProtector _protector;

    public WebhookSubscriptionService(ApplicationDbContext db, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("Webhooks.Url.v1");
    }

    public async Task<WebhookSettingsDto> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.WebhookSubscriptions.AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return new WebhookSettingsDto(
            rows.Select(ToDto).ToList(),
            new WebhookDefaultTemplatesDto(
                WebhookMessageRenderer.DefaultCreated,
                WebhookMessageRenderer.DefaultMerged,
                WebhookMessageRenderer.RenderSample()),
            WebhookMessageRenderer.Placeholders
                .Select(p => new WebhookPlaceholderDto(p.Token, p.Description))
                .ToList());
    }

    public async Task<WebhookSubscriptionDto> CreateAsync(
        CreateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var name = NormalizeName(request.Name);
        ValidateTemplates(request.TemplateCreated, request.TemplateMerged);

        if (!WebhookUrlRules.TryValidate(request.Kind, request.Url, out var urlError))
            throw new ArgumentException(urlError);

        var count = await _db.WebhookSubscriptions.CountAsync(cancellationToken);
        if (count >= MaxDestinations)
            throw new InvalidOperationException($"At most {MaxDestinations} chat destinations can be configured.");

        var now = DateTimeOffset.UtcNow;
        var entity = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            Name = name,
            Kind = request.Kind,
            Enabled = request.Enabled,
            ProtectedUrl = ProtectUrl(request.Url.Trim()),
            UrlPreview = WebhookUrlRules.Mask(request.Url),
            NotifyOnDevToMasterCreated = request.NotifyOnDevToMasterCreated,
            NotifyOnDevToMasterMerged = request.NotifyOnDevToMasterMerged,
            NotifyOnMasterToProdCreated = request.NotifyOnMasterToProdCreated,
            NotifyOnMasterToProdMerged = request.NotifyOnMasterToProdMerged,
            TemplateCreated = NormalizeTemplate(request.TemplateCreated),
            TemplateMerged = NormalizeTemplate(request.TemplateMerged),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.WebhookSubscriptions.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<WebhookSubscriptionDto?> UpdateAsync(
        Guid id,
        UpdateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null)
            return null;

        entity.Name = NormalizeName(request.Name);
        entity.Enabled = request.Enabled;
        entity.NotifyOnDevToMasterCreated = request.NotifyOnDevToMasterCreated;
        entity.NotifyOnDevToMasterMerged = request.NotifyOnDevToMasterMerged;
        entity.NotifyOnMasterToProdCreated = request.NotifyOnMasterToProdCreated;
        entity.NotifyOnMasterToProdMerged = request.NotifyOnMasterToProdMerged;

        ValidateTemplates(request.TemplateCreated, request.TemplateMerged);

        entity.TemplateCreated = NormalizeTemplate(request.TemplateCreated);
        entity.TemplateMerged = NormalizeTemplate(request.TemplateMerged);

        if (!string.IsNullOrWhiteSpace(request.Url))
        {
            if (!WebhookUrlRules.TryValidate(entity.Kind, request.Url, out var urlError))
                throw new ArgumentException(urlError);
            entity.ProtectedUrl = ProtectUrl(request.Url.Trim());
            entity.UrlPreview = WebhookUrlRules.Mask(request.Url);
        }

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null)
            return false;

        _db.WebhookSubscriptions.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<WebhookSubscription>> ListEnabledForEventAsync(
        WebhookEventKind eventKind,
        CancellationToken cancellationToken)
    {
        var query = _db.WebhookSubscriptions.AsNoTracking().Where(s => s.Enabled);
        query = eventKind switch
        {
            WebhookEventKind.DevToMasterCreated => query.Where(s => s.NotifyOnDevToMasterCreated),
            WebhookEventKind.DevToMasterMerged => query.Where(s => s.NotifyOnDevToMasterMerged),
            WebhookEventKind.MasterToProdCreated => query.Where(s => s.NotifyOnMasterToProdCreated),
            WebhookEventKind.MasterToProdMerged => query.Where(s => s.NotifyOnMasterToProdMerged),
            _ => query.Where(_ => false)
        };
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<WebhookSubscription?> GetTrackedAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public string UnprotectUrl(byte[] protectedBytes)
    {
        try
        {
            return Encoding.UTF8.GetString(_protector.Unprotect(protectedBytes));
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Stored webhook URL could not be decrypted (data protection keys may have changed). Re-save the URL.",
                ex);
        }
    }

    public async Task RecordDeliveryAsync(
        Guid id,
        bool ok,
        string? error,
        CancellationToken cancellationToken)
    {
        var entity = await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null)
            return;

        var now = DateTimeOffset.UtcNow;
        entity.UpdatedAt = now;
        if (ok)
        {
            entity.LastDeliveredAt = now;
            entity.LastDeliveryError = null;
        }
        else
        {
            entity.LastDeliveryError = TruncateError(error);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private byte[] ProtectUrl(string url) => _protector.Protect(Encoding.UTF8.GetBytes(url));

    private static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw new ArgumentException("Name is required.");
        if (trimmed.Length > MaxNameLength)
            throw new ArgumentException($"Name must be at most {MaxNameLength} characters.");
        return trimmed;
    }

    private static void ValidateTemplates(params string?[] templates)
    {
        foreach (var template in templates)
        {
            if (template is { Length: > MaxTemplateLength })
                throw new ArgumentException($"Message template must be at most {MaxTemplateLength} characters.");
        }
    }

    private static string? NormalizeTemplate(string? template)
    {
        var trimmed = template?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? TruncateError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "Delivery failed.";
        var trimmed = error.Trim();
        return trimmed.Length <= 2000 ? trimmed : trimmed[..1997] + "...";
    }

    private static WebhookSubscriptionDto ToDto(WebhookSubscription s) =>
        new(
            s.Id,
            s.Name,
            s.Kind,
            s.Enabled,
            s.UrlPreview,
            s.NotifyOnDevToMasterCreated,
            s.NotifyOnDevToMasterMerged,
            s.NotifyOnMasterToProdCreated,
            s.NotifyOnMasterToProdMerged,
            s.TemplateCreated,
            s.TemplateMerged,
            s.CreatedAt,
            s.UpdatedAt,
            s.LastDeliveredAt,
            s.LastDeliveryError);
}
