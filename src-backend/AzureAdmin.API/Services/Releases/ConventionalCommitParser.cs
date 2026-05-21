using System.Text.RegularExpressions;
using AzureAdmin.API.Contracts;

namespace AzureAdmin.API.Services.Releases;

public sealed class ConventionalCommitParser
{
    private static readonly Regex CommitRegex = new(
        @"^(?<type>[a-zA-Z]+)(\((?<scope>[^)]+)\))?(?<breaking>!)?\s*:\s*(?<description>.+)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex BreakingFooterRegex = new(
        @"^BREAKING[- ]CHANGE\s*:",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Dictionary<string, string> TypeToGroup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["feat"] = "Features",
        ["fix"] = "Bug Fixes",
        ["perf"] = "Performance",
        ["refactor"] = "Refactoring",
        ["docs"] = "Documentation",
        ["test"] = "Tests",
        ["chore"] = "Chores",
        ["build"] = "Chores",
        ["ci"] = "Chores",
        ["style"] = "Style",
        ["revert"] = "Reverts",
    };

    private static readonly string[] GroupOrder =
    [
        "Breaking Changes", "Features", "Bug Fixes", "Performance", "Refactoring",
        "Documentation", "Tests", "Chores", "Style", "Reverts", "Other"
    ];

    private static readonly Dictionary<string, string> GroupEmojis = new()
    {
        ["Breaking Changes"] = "⚠️",
        ["Features"] = "✨",
        ["Bug Fixes"] = "🐛",
        ["Performance"] = "⚡️",
        ["Refactoring"] = "♻️",
        ["Documentation"] = "📚",
        ["Tests"] = "🧪",
        ["Chores"] = "🔧",
        ["Style"] = "💄",
        ["Reverts"] = "⏪",
        ["Other"] = "📝",
    };

    public ParsedConventionalCommit Parse(string message)
    {
        var firstLine = (message ?? "").Split('\n', 2)[0].Trim();
        var match = CommitRegex.Match(firstLine);

        if (!match.Success)
            return new ParsedConventionalCommit(null, null, message ?? "", false, message ?? "", "Other");

        var type = match.Groups["type"].Value;
        var scope = match.Groups["scope"].Success ? match.Groups["scope"].Value : null;
        var description = match.Groups["description"].Value.Trim();
        var isBreaking = match.Groups["breaking"].Success || BreakingFooterRegex.IsMatch(message ?? "");
        var group = TypeToGroup.TryGetValue(type, out var g) ? g : "Other";

        return new ParsedConventionalCommit(type, scope, description, isBreaking, message ?? "", group);
    }

    public IReadOnlyList<CommitGroupDto> BuildGroups(
        IEnumerable<(ReleaseCommitItemDto Commit, ParsedConventionalCommit Parsed, IReadOnlyList<JiraTicketRefDto> JiraRefs)> items,
        bool useEmojis,
        bool showOtherGroup)
    {
        var buckets = new Dictionary<string, List<EnrichedCommitItemDto>>(StringComparer.Ordinal);

        foreach (var (commit, parsed, jiraRefs) in items)
        {
            var groupKey = parsed.IsBreaking ? "Breaking Changes" : parsed.GroupName;

            if (!showOtherGroup && groupKey == "Other")
                continue;

            if (!buckets.TryGetValue(groupKey, out var list))
            {
                list = [];
                buckets[groupKey] = list;
            }

            list.Add(new EnrichedCommitItemDto(
                commit.CommitId,
                commit.AuthorName,
                commit.CommittedDate,
                commit.Comment,
                parsed.Type,
                parsed.Scope,
                parsed.Description,
                parsed.IsBreaking,
                jiraRefs));
        }

        return GroupOrder
            .Where(buckets.ContainsKey)
            .Select(groupKey =>
            {
                var displayName = useEmojis && GroupEmojis.TryGetValue(groupKey, out var emoji)
                    ? $"{emoji} {groupKey}"
                    : groupKey;
                return new CommitGroupDto(displayName, groupKey == "Breaking Changes", buckets[groupKey]);
            })
            .ToList();
    }
}

public sealed record ParsedConventionalCommit(
    string? Type,
    string? Scope,
    string Description,
    bool IsBreaking,
    string RawMessage,
    string GroupName);
