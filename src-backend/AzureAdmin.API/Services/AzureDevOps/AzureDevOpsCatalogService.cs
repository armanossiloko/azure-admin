using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AzureAdmin.API.Configuration;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Services.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AzureAdmin.API.Services.AzureDevOps;

public sealed class AzureDevOpsCatalogService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAzureDevOpsPatResolver _patResolver;
    private readonly ICurrentUser _currentUser;
    private readonly AzureDevOpsOptions _options;

    public AzureDevOpsCatalogService(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        IAzureDevOpsPatResolver patResolver,
        ICurrentUser currentUser,
        IOptions<AzureDevOpsOptions> options)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _patResolver = patResolver;
        _currentUser = currentUser;
        _options = options.Value;
    }

    private string ApiVersion =>
        string.IsNullOrWhiteSpace(_options.ApiVersion) ? "7.1" : _options.ApiVersion.Trim();

    public async Task<IReadOnlyList<AzureCatalogProjectDto>> ListProjectsAsync(
        Guid userOrganizationId,
        CancellationToken cancellationToken)
    {
        var (orgSegment, userId) = await ResolveOrganizationAsync(userOrganizationId, cancellationToken);
        var pat = await _patResolver.ResolvePatForOrganizationAsync(userId, orgSegment, cancellationToken);

        var url =
            $"https://dev.azure.com/{Uri.EscapeDataString(orgSegment)}/_apis/projects?api-version={Uri.EscapeDataString(ApiVersion)}&$top=500";

        var json = await GetJsonAsync(pat, url, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<AzureCatalogProjectDto>();

        var list = new List<AzureCatalogProjectDto>();
        foreach (var el in arr.EnumerateArray())
        {
            var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var name = el.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                continue;
            list.Add(new AzureCatalogProjectDto(id, name));
        }

        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<AzureCatalogRepositoryDto>> ListRepositoriesAsync(
        Guid userOrganizationId,
        string projectName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("Project name is required.", nameof(projectName));

        var (orgSegment, userId) = await ResolveOrganizationAsync(userOrganizationId, cancellationToken);
        var pat = await _patResolver.ResolvePatForOrganizationAsync(userId, orgSegment, cancellationToken);

        var url =
            $"https://dev.azure.com/{Uri.EscapeDataString(orgSegment)}/{Uri.EscapeDataString(projectName.Trim())}/_apis/git/repositories?api-version={Uri.EscapeDataString(ApiVersion)}&$top=500";

        var json = await GetJsonAsync(pat, url, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<AzureCatalogRepositoryDto>();

        var list = new List<AzureCatalogRepositoryDto>();
        foreach (var el in arr.EnumerateArray())
        {
            var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var name = el.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                continue;

            var projName = projectName.Trim();
            if (el.TryGetProperty("project", out var proj) && proj.TryGetProperty("name", out var pn))
            {
                var p = pn.GetString();
                if (!string.IsNullOrWhiteSpace(p))
                    projName = p;
            }

            list.Add(new AzureCatalogRepositoryDto(id, name, projName));
        }

        return list.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<(string OrgSegment, Guid UserId)> ResolveOrganizationAsync(
        Guid userOrganizationId,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetRequiredUserId();
        var org = await _db.UserAzureDevOpsOrganizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == userOrganizationId && o.UserId == userId, cancellationToken);
        if (org is null)
            throw new ArgumentException("Organization was not found.", nameof(userOrganizationId));

        return (org.OrganizationKey, userId);
    }

    private static AuthenticationHeaderValue PatAuth(string pat)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private async Task<string> GetJsonAsync(string pat, string url, CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Authorization = PatAuth(pat);

        using var resp = await http.GetAsync(url, cancellationToken);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Azure DevOps request failed ({(int)resp.StatusCode}): {text}");

        return text;
    }
}
