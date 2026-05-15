using System.Text.Json;
using AzureAdmin.Api.Common;
using AzureAdmin.Api.Contracts;

namespace AzureAdmin.Api.Services.Releases;

/// <summary>Persisted commit row shape (explicit properties for reliable JSON round-trip).</summary>
internal sealed class StoredCommitNoteItem
{
    public string CommitId { get; set; } = "";
    public string Comment { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public DateTimeOffset CommittedDate { get; set; }
}

internal static class ReleaseCommitJson
{
    public static string Serialize(IReadOnlyList<ReleaseCommitItemDto> items)
    {
        var stored = items.Select(i => new StoredCommitNoteItem
        {
            CommitId = i.CommitId,
            Comment = i.Comment,
            AuthorName = i.AuthorName,
            CommittedDate = i.CommittedDate
        }).ToList();

        return JsonSerializer.Serialize(stored, SystemTextJsonDefaults.CamelCase);
    }

    public static List<ReleaseCommitItemDto> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
            return new List<ReleaseCommitItemDto>();

        try
        {
            var stored = JsonSerializer.Deserialize<List<StoredCommitNoteItem>>(json, SystemTextJsonDefaults.CamelCase);
            if (stored is { Count: > 0 })
            {
                return stored
                    .Where(s => !string.IsNullOrWhiteSpace(s.CommitId))
                    .Select(s => new ReleaseCommitItemDto(
                        s.CommitId,
                        s.Comment ?? "",
                        s.AuthorName ?? "",
                        s.CommittedDate))
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // fall through to flexible parse
        }

        return DeserializeFlexible(json);
    }

    /// <summary>Handles legacy rows serialized from record DTOs or PascalCase property names.</summary>
    private static List<ReleaseCommitItemDto> DeserializeFlexible(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return new List<ReleaseCommitItemDto>();

        var list = new List<ReleaseCommitItemDto>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var id = GetString(el, "commitId");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var comment = GetString(el, "comment") ?? "";
            var author = GetString(el, "authorName") ?? "";
            var dateStr = GetString(el, "committedDate");
            _ = DateTimeOffset.TryParse(dateStr, out var when);

            list.Add(new ReleaseCommitItemDto(id, comment, author, when));
        }

        return list;
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();

        foreach (var p in el.EnumerateObject())
        {
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString();
        }

        return null;
    }
}
