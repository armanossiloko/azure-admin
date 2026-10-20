using System.Text;
using System.Text.RegularExpressions;
using AzureAdmin.API.Models;

namespace AzureAdmin.API.Services.Webhooks;

public sealed record WebhookPrLine(
    string TeamName,
    string DisplayName,
    int PullRequestId,
    string Url,
    string? FailureMessage = null);

public sealed record WebhookMessageContext(
    WebhookEventKind Event,
    string ReleaseTitle,
    string? SprintLabel,
    string PhaseLabel,
    string? ReleaseUrl,
    string? Actor,
    IReadOnlyList<WebhookPrLine> PullRequests,
    IReadOnlyList<WebhookPrLine> FailedPullRequests);

public static partial class WebhookMessageRenderer
{
    public const string DefaultCreated = """
        Created {{pr_count}} PRs for {{phase}} targeting {{team_count}} teams:
        {{pr_list}}
        """;

    public const string DefaultMerged = """
        Merged {{pr_count}} PRs for {{phase}} targeting {{team_count}} teams:
        {{pr_list}}
        {{failed_section}}
        """;

    private static readonly Regex Placeholder = PlaceholderPattern();

    public static WebhookMessageContext SampleContext { get; } = new(
        WebhookEventKind.DevToMasterCreated,
        "Release sprint 2026/41",
        "2026/41",
        PhaseLabel(ReleasePrPhase.DevToMaster),
        "https://example.local/releases/example",
        "Ada",
        [
            new WebhookPrLine("backend", "pr1", 1, "https://dev.azure.com/example/_git/svc/pullrequest/1"),
            new WebhookPrLine("backend", "pr2", 2, "https://dev.azure.com/example/_git/svc/pullrequest/2"),
            new WebhookPrLine("frontend", "pr3", 3, "https://dev.azure.com/example/_git/web/pullrequest/3"),
            new WebhookPrLine("frontend", "pr4", 4, "https://dev.azure.com/example/_git/web/pullrequest/4"),
            new WebhookPrLine("frontend", "pr5", 5, "https://dev.azure.com/example/_git/web/pullrequest/5")
        ],
        []);

    public static string DefaultTemplate(WebhookEventKind eventKind) =>
        WebhookEvents.IsMerged(eventKind) ? DefaultMerged : DefaultCreated;

    public static string ResolveTemplate(string? stored, WebhookEventKind eventKind)
    {
        var trimmed = stored?.Trim();
        return string.IsNullOrEmpty(trimmed) ? DefaultTemplate(eventKind) : trimmed;
    }

    public static string Render(string template, WebhookMessageContext context, bool markdownLinks)
    {
        var values = BuildValues(context, markdownLinks);
        return Placeholder.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var value) ? value : match.Value;
        }).Trim();
    }

    public static string RenderSample(string? template = null) =>
        Render(string.IsNullOrWhiteSpace(template) ? DefaultCreated : template.Trim(), SampleContext, markdownLinks: false);

    public static IReadOnlyList<(string Token, string Description)> Placeholders { get; } =
    [
        ("{{pr_count}}", "Number of pull requests in this message"),
        ("{{team_count}}", "Number of distinct teams"),
        ("{{phase}}", "Phase label, e.g. dev → master"),
        ("{{action}}", "Created or Merged"),
        ("{{release_title}}", "Release title"),
        ("{{sprint}}", "Sprint label, or empty"),
        ("{{release_url}}", "Link to the release in Azure Admin"),
        ("{{actor}}", "Name of the signed-in user who triggered the action"),
        ("{{pr_list}}", "One PR per line: - name - team (linked when the destination supports it)"),
        ("{{pr_list_plain}}", "Same list without links"),
        ("{{failed_count}}", "How many PRs failed (complete-batch)"),
        ("{{failed_list}}", "Failed PRs in the same list format"),
        ("{{failed_section}}", "Failed block, or empty when nothing failed")
    ];

    public static string PhaseLabel(ReleasePrPhase phase) =>
        phase switch
        {
            ReleasePrPhase.DevToMaster => "dev → master",
            ReleasePrPhase.MasterToProd => "master → prod",
            ReleasePrPhase.ProdToDev => "prod → dev",
            _ => phase.ToString()
        };

    private static Dictionary<string, string> BuildValues(WebhookMessageContext context, bool markdownLinks)
    {
        var teams = context.PullRequests
            .Select(p => p.TeamName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var failedCount = context.FailedPullRequests.Count;
        var failedList = FormatPrList(context.FailedPullRequests, markdownLinks, includeFailure: true);
        var failedSection = failedCount == 0
            ? ""
            : $"Failed ({failedCount}):\n{failedList}";

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pr_count"] = context.PullRequests.Count.ToString(),
            ["team_count"] = teams.ToString(),
            ["phase"] = context.PhaseLabel,
            ["action"] = WebhookEvents.IsMerged(context.Event) ? "Merged" : "Created",
            ["release_title"] = context.ReleaseTitle,
            ["sprint"] = context.SprintLabel ?? "",
            ["release_url"] = context.ReleaseUrl ?? "",
            ["actor"] = context.Actor ?? "",
            ["pr_list"] = FormatPrList(context.PullRequests, markdownLinks, includeFailure: false),
            ["pr_list_plain"] = FormatPrList(context.PullRequests, markdownLinks: false, includeFailure: false),
            ["failed_count"] = failedCount.ToString(),
            ["failed_list"] = failedList,
            ["failed_section"] = failedSection
        };
    }

    public static string FormatPrList(
        IReadOnlyList<WebhookPrLine> items,
        bool markdownLinks,
        bool includeFailure)
    {
        if (items.Count == 0)
            return "(none)";

        var ordered = items
            .OrderBy(p => p.TeamName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        foreach (var pr in ordered)
        {
            var label = string.IsNullOrWhiteSpace(pr.DisplayName) ? $"#{pr.PullRequestId}" : pr.DisplayName;
            var name = markdownLinks && !string.IsNullOrWhiteSpace(pr.Url)
                ? $"[{label}]({pr.Url})"
                : label;
            var line = $"- {name} - {pr.TeamName}";
            if (includeFailure && !string.IsNullOrWhiteSpace(pr.FailureMessage))
                line += $" — {pr.FailureMessage}";
            sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd();
    }

    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
