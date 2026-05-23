using System.Text.Json;

namespace AzureAdmin.API.Services.AzureDevOps;

internal static class GitCommitJsonParser
{
    public static IReadOnlyList<AzureDevOpsCommitBrief> ParseCommitArray(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
            return Array.Empty<AzureDevOpsCommitBrief>();

        var list = new List<AzureDevOpsCommitBrief>();
        foreach (var el in array.EnumerateArray())
        {
            var parsed = TryParseCommit(el);
            if (parsed is not null)
                list.Add(parsed);
        }

        return list;
    }

    public static AzureDevOpsCommitBrief? TryParseCommit(JsonElement el)
    {
        var id = GetString(el, "commitId");
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var comment = GetString(el, "comment") ?? "";
        var authorName = "";
        DateTimeOffset when = default;

        if (TryGetObject(el, "committer", out var committer))
        {
            authorName = GetString(committer, "name") ?? "";
            when = GetDate(committer, "date") ?? default;
        }

        if (string.IsNullOrEmpty(authorName) && TryGetObject(el, "author", out var author))
        {
            authorName = GetString(author, "name") ?? "";
            if (when == default)
                when = GetDate(author, "date") ?? default;
        }

        return new AzureDevOpsCommitBrief(id, comment.Trim(), authorName, when);
    }

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement obj)
    {
        if (TryGetProperty(parent, name, out var prop) && prop.ValueKind == JsonValueKind.Object)
        {
            obj = prop;
            return true;
        }

        obj = default;
        return false;
    }

    private static string? GetString(JsonElement parent, string name)
    {
        if (!TryGetProperty(parent, name, out var prop))
            return null;

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Null => null,
            _ => prop.ToString()
        };
    }

    private static DateTimeOffset? GetDate(JsonElement parent, string name)
    {
        var s = GetString(parent, name);
        return DateTimeOffset.TryParse(s, out var parsed) ? parsed : null;
    }

    private static bool TryGetProperty(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.TryGetProperty(name, out value))
            return true;

        foreach (var prop in parent.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
