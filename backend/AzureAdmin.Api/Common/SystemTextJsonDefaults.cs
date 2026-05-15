using System.Text.Json;

namespace AzureAdmin.Api.Common;

public static class SystemTextJsonDefaults
{
    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
