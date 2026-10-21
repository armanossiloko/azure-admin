using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AzureAdmin.API.Infrastructure;

/// <summary>
/// Keeps request bodies to <c>application/json</c> in the contract. ASP.NET Core advertises the
/// <c>application/*+json</c> wildcard too, and the client generator would otherwise pick that as the
/// Content-Type it sends.
/// </summary>
public sealed class JsonOnlyRequestBodyOperationFilter : IOperationFilter
{
    private const string Json = "application/json";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.RequestBody?.Content is not { Count: > 1 } content || !content.ContainsKey(Json))
            return;

        foreach (var mediaType in content.Keys.Where(k => k != Json).ToList())
            content.Remove(mediaType);
    }
}
