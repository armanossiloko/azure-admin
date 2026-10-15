using System.Text.RegularExpressions;

namespace AzureAdmin.API.Services.Releases;

public sealed class JiraReferenceExtractor
{
    public IReadOnlyList<string> ExtractKeys(string message, IEnumerable<string> projectKeys)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];

        var keys = projectKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (keys.Count == 0)
            return [];

        // Longer prefixes first so e.g. BB_DEV is tried before BB when both are configured.
        keys.Sort((a, b) => b.Length.CompareTo(a.Length));

        var alternation = string.Join('|', keys.Select(Regex.Escape));
        var pattern = $@"\b(?:{alternation})-\d+\b";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        return regex.Matches(message)
            .Select(m => m.Value.ToUpperInvariant())
            .Distinct()
            .ToList();
    }
}
