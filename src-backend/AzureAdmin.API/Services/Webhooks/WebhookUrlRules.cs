using AzureAdmin.API.Models;

namespace AzureAdmin.API.Services.Webhooks;

/// <summary>
/// Validates incoming-webhook URLs so the API does not POST to arbitrary internal hosts (SSRF).
/// </summary>
public static class WebhookUrlRules
{
    public const int MaxUrlLength = 2048;

    public static bool TryValidate(WebhookDestinationKind kind, string url, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "Webhook URL is required.";
            return false;
        }

        var trimmed = url.Trim();
        if (trimmed.Length > MaxUrlLength)
        {
            error = $"Webhook URL must be at most {MaxUrlLength} characters.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            error = "Webhook URL is not a valid absolute URL.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !IsLocalHttp(uri))
        {
            error = "Webhook URL must use HTTPS.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "Webhook URL must not include a username or password.";
            return false;
        }

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            error = "Webhook URL must use a hostname, not an IP address.";
            return false;
        }

        if (!HostAllowed(kind, uri.Host))
        {
            error = kind switch
            {
                WebhookDestinationKind.Teams =>
                    "Teams webhook host must be webhook.office.com, *.logic.azure.com, or a Power Automate / Power Platform host.",
                WebhookDestinationKind.Slack =>
                    "Slack webhook host must be hooks.slack.com.",
                WebhookDestinationKind.Discord =>
                    "Discord webhook host must be discord.com or discordapp.com.",
                _ => "Unsupported webhook destination."
            };
            return false;
        }

        if (kind == WebhookDestinationKind.Discord
            && !uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.OrdinalIgnoreCase))
        {
            error = "Discord webhook path must start with /api/webhooks/.";
            return false;
        }

        return true;
    }

    public static string Mask(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return "••••";

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string path;
        if (segments.Length == 0)
            path = "";
        else if (segments.Length == 1)
            path = "/" + MaskSegment(segments[0]);
        else
            path = "/" + MaskSegment(segments[0]) + "/••••";

        var port = uri.IsDefaultPort ? "" : ":" + uri.Port;
        return $"{uri.Scheme}://{uri.Host}{port}{path}";
    }

    private static bool IsLocalHttp(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase));

    private static bool HostAllowed(WebhookDestinationKind kind, string host) =>
        kind switch
        {
            WebhookDestinationKind.Slack => HostIs(host, "hooks.slack.com"),
            WebhookDestinationKind.Discord =>
                HostIs(host, "discord.com")
                || HostIs(host, "discordapp.com")
                || HostIs(host, "ptb.discord.com")
                || HostIs(host, "canary.discord.com"),
            WebhookDestinationKind.Teams =>
                HostIs(host, "webhook.office.com")
                || HostEndsWith(host, "webhook.office.com")
                || HostIs(host, "outlook.office.com")
                || HostEndsWith(host, "logic.azure.com")
                || HostEndsWith(host, "environment.api.powerplatform.com")
                || HostEndsWith(host, "api.powerplatform.com")
                || HostEndsWith(host, "powerautomate.com"),
            _ => false
        };

    private static bool HostIs(string host, string expected) =>
        string.Equals(host, expected, StringComparison.OrdinalIgnoreCase);

    private static bool HostEndsWith(string host, string suffix) =>
        host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);

    private static string MaskSegment(string segment) =>
        segment.Length <= 4 ? segment : segment[..2] + "•••";
}
