using System.Text.RegularExpressions;

namespace AzureAdmin.API.Services.Releases;

public sealed class JiraReferenceExtractor
{
    public IReadOnlyList<string> ExtractKeys(string message, string projectKey)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(projectKey))
            return [];

        var pattern = $@"\b{Regex.Escape(projectKey)}-\d+\b";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        return regex.Matches(message)
            .Select(m => m.Value.ToUpperInvariant())
            .Distinct()
            .ToList();
    }
}
