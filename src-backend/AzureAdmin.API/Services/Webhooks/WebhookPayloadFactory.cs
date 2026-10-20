using System.Text.Json;
using System.Text.RegularExpressions;
using AzureAdmin.API.Models;

namespace AzureAdmin.API.Services.Webhooks;

public static partial class WebhookPayloadFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static (string MediaType, string Body) Create(WebhookDestinationKind kind, string markdownMessage)
    {
        return kind switch
        {
            WebhookDestinationKind.Slack => ("application/json", JsonSerializer.Serialize(
                new { text = ToSlackMrkdwn(markdownMessage) }, JsonOptions)),
            WebhookDestinationKind.Discord => ("application/json", JsonSerializer.Serialize(
                new { content = Truncate(markdownMessage, 2000) }, JsonOptions)),
            // Teams Workflows incoming webhooks accept the same shape as Slack's text hook.
            // https://learn.microsoft.com/en-us/microsoftteams/platform/webhooks-and-connectors/how-to/add-incoming-webhook
            WebhookDestinationKind.Teams => ("application/json", JsonSerializer.Serialize(
                new { text = markdownMessage }, JsonOptions)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported destination.")
        };
    }

    internal static string ToSlackMrkdwn(string markdown)
    {
        var links = MarkdownLinkPattern().Replace(markdown, m => $"<{m.Groups[2].Value}|{m.Groups[1].Value}>");
        return BoldPattern().Replace(links, m => $"*{m.Groups[1].Value}*");
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;
        const string suffix = "\n…(truncated)";
        var keep = Math.Max(0, max - suffix.Length);
        return value[..keep] + suffix;
    }

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkPattern();

    [GeneratedRegex(@"\*\*([^*]+)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex BoldPattern();
}
