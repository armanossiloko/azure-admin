using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace AzureAdmin.API.DependencyInjection;

public static class MvcServiceCollectionExtensions
{
    /// <summary>
    /// JSON responses so Swashbuckle does not emit text/plain variants. Do not add a global
    /// <c>[Consumes]</c> filter: with <c>MapFallbackToFile</c> mapped, every request that has no
    /// Content-Type is discarded by AcceptsMatcherPolicy and served as the SPA instead
    /// (dotnet/aspnetcore#41060), which is NG04002 on <c>/api/auth/login</c>.
    /// </summary>
    public static IMvcBuilder AddApiControllers(this IServiceCollection services)
    {
        return services.AddControllers(options =>
            {
                options.Filters.Add(new ProducesAttribute("application/json"));
            })
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
    }
}
