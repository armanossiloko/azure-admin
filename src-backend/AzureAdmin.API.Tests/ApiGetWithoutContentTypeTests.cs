using System.Net;
using AzureAdmin.API.Controllers.Health;
using AzureAdmin.API.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace AzureAdmin.API.Tests;

/// <summary>
/// A global <c>[Consumes("application/json")]</c> plus <c>MapFallbackToFile</c> makes every API GET
/// without a Content-Type return the SPA (dotnet/aspnetcore#41060). That is NG04002 on login.
/// </summary>
public class ApiGetWithoutContentTypeTests
{
    [Fact]
    public async Task Get_api_health_without_content_type_returns_json_not_the_spa()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "azure-admin-spa-fallback-" + Guid.NewGuid().ToString("N"));
        var wwwroot = Path.Combine(contentRoot, "wwwroot");
        Directory.CreateDirectory(wwwroot);
        await File.WriteAllTextAsync(Path.Combine(wwwroot, "index.html"), "<html>spa</html>");

        await using var app = CreateApp(contentRoot);
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
            Assert.Null(request.Content);

            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"ok\":true", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("spa", body, StringComparison.Ordinal);
        }
        finally
        {
            await app.StopAsync();
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static WebApplication CreateApp(string contentRoot)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            EnvironmentName = "Development",
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddApiControllers()
            .AddApplicationPart(typeof(HealthController).Assembly);

        var app = builder.Build();
        app.MapControllers();
        app.MapFallbackToFile("index.html");
        return app;
    }
}
