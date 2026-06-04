namespace AzureAdmin.API.Contracts.Search;

public sealed record SearchResultDto(
    IReadOnlyList<SearchHitDto> Hits);

public sealed record SearchHitDto(
    string Kind,
    string Title,
    string? Subtitle,
    string Href);
