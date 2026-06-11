using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AzureAdmin.API.Configuration;
using Microsoft.Extensions.Options;

namespace AzureAdmin.API.Services.AzureDevOps;

public sealed class AzureDevOpsClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureDevOpsOptions _options;
    private readonly IAzureDevOpsPatResolver _patResolver;

    public AzureDevOpsClient(
        IHttpClientFactory httpClientFactory,
        IOptions<AzureDevOpsOptions> options,
        IAzureDevOpsPatResolver patResolver)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _patResolver = patResolver;
    }

    public async Task<AzureDevOpsCreatePullRequestResponse> CreatePullRequestAsync(
        Guid userId,
        string organization,
        string project,
        string repositoryIdOrName,
        string sourceRefName,
        string targetRefName,
        string title,
        string? description,
        CancellationToken ct)
    {
        var pat = await _patResolver.ResolvePatForOrganizationAsync(userId, organization, ct);

        // Azure DevOps expects full ref names: refs/heads/dev, refs/heads/master, etc.
        sourceRefName = NormalizeRefName(sourceRefName);
        targetRefName = NormalizeRefName(targetRefName);

        var url =
            $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryIdOrName)}/pullrequests?api-version={Uri.EscapeDataString(_options.ApiVersion)}";

        var body = new
        {
            sourceRefName,
            targetRefName,
            title,
            description
        };

        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Authorization = CreatePatAuthHeader(pat);

        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync(url, content, ct);
        var respText = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Azure DevOps PR create failed ({(int)resp.StatusCode}): {respText}");

        var parsed = JsonSerializer.Deserialize<AzureDevOpsCreatePullRequestResponse>(
            respText,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (parsed is null)
            throw new InvalidOperationException("Azure DevOps response could not be parsed.");

        // The response doesn't directly include a browser URL; construct one.
        var prUrl =
            $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_git/{Uri.EscapeDataString(repositoryIdOrName)}/pullrequest/{parsed.PullRequestId}";

        return parsed with { Url = prUrl };
    }

    /// <summary>
    /// Returns the pull request <c>status</c> field from Azure DevOps (typically <c>active</c>, <c>abandoned</c>, <c>completed</c>),
    /// or <c>null</c> if the PR no longer exists (404).
    /// </summary>
    public async Task<string?> TryGetGitPullRequestStatusAsync(
        Guid userId,
        string organization,
        string project,
        string repositoryIdOrName,
        int pullRequestId,
        CancellationToken ct)
    {
        var pat = await _patResolver.ResolvePatForOrganizationAsync(userId, organization, ct);

        var url =
            $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryIdOrName)}/pullrequests/{pullRequestId}?api-version={Uri.EscapeDataString(_options.ApiVersion)}";

        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Authorization = CreatePatAuthHeader(pat);

        using var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Azure DevOps get PR failed ({(int)resp.StatusCode}): {text}");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (!root.TryGetProperty("status", out var statusEl))
            return "";

        return statusEl.ValueKind switch
        {
            JsonValueKind.String => statusEl.GetString(),
            JsonValueKind.Number => statusEl.GetInt32().ToString(),
            _ => statusEl.ToString()
        };
    }

    /// <summary>Commits included in a pull request (preferred source for release notes).</summary>
    public async Task<IReadOnlyList<AzureDevOpsCommitBrief>> GetPullRequestCommitsAsync(
        Guid userId,
        string organization,
        string project,
        string repositoryIdOrName,
        int pullRequestId,
        int top,
        CancellationToken ct)
    {
        var pat = await _patResolver.ResolvePatForOrganizationAsync(userId, organization, ct);

        var qs =
            $"api-version={Uri.EscapeDataString(_options.ApiVersion)}" +
            $"&$top={top}";

        var url =
            $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryIdOrName)}/pullrequests/{pullRequestId}/commits?{qs}";

        return await GetCommitListFromUrlAsync(pat, url, ct);
    }

    /// <summary>
    /// Commits reachable from <paramref name="sourceBranch"/> but not from <paramref name="targetBranch"/>
    /// (short branch names without refs/heads/).
    /// </summary>
    public async Task<IReadOnlyList<AzureDevOpsCommitBrief>> GetCommitsBetweenBranchesAsync(
        Guid userId,
        string organization,
        string project,
        string repositoryIdOrName,
        string sourceBranch,
        string targetBranch,
        int top,
        CancellationToken ct)
    {
        var pat = await _patResolver.ResolvePatForOrganizationAsync(userId, organization, ct);
        sourceBranch = sourceBranch.Trim();
        targetBranch = targetBranch.Trim();

        return await FetchCommitsBetweenBranchesAsync(
            pat, organization, project, repositoryIdOrName, sourceBranch, targetBranch, top, ct);
    }

    private async Task<IReadOnlyList<AzureDevOpsCommitBrief>> FetchCommitsBetweenBranchesAsync(
        string pat,
        string organization,
        string project,
        string repositoryIdOrName,
        string itemBranch,
        string compareBranch,
        int top,
        CancellationToken ct)
    {
        var qs =
            $"api-version={Uri.EscapeDataString(_options.ApiVersion)}" +
            $"&$top={top}" +
            $"&searchCriteria.itemVersion.version={Uri.EscapeDataString(itemBranch)}" +
            "&searchCriteria.itemVersion.versionType=branch" +
            $"&searchCriteria.compareVersion.version={Uri.EscapeDataString(compareBranch)}" +
            "&searchCriteria.compareVersion.versionType=branch";

        var url =
            $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryIdOrName)}/commits?{qs}";

        return await GetCommitListFromUrlAsync(pat, url, ct);
    }

    private async Task<IReadOnlyList<AzureDevOpsCommitBrief>> GetCommitListFromUrlAsync(
        string pat,
        string url,
        CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Authorization = CreatePatAuthHeader(pat);

        using var resp = await http.GetAsync(url, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Azure DevOps commits failed ({(int)resp.StatusCode}): {text}");

        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<AzureDevOpsCommitBrief>();

        return GitCommitJsonParser.ParseCommitArray(arr);
    }

    private static AuthenticationHeaderValue CreatePatAuthHeader(string pat)
    {
        // Basic auth: username can be empty; PAT is the password.
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    public async Task<IReadOnlyList<AzureDevOpsGitRef>> ListBranchRefsAsync(
        Guid userId,
        string organization,
        string project,
        string repositoryIdOrName,
        CancellationToken ct)
    {
        var pat = await _patResolver.ResolvePatForOrganizationAsync(userId, organization, ct);
        var results = new List<AzureDevOpsGitRef>();
        string? continuationToken = null;

        do
        {
            var qs =
                $"filter=heads/" +
                $"&api-version={Uri.EscapeDataString(_options.ApiVersion)}" +
                (continuationToken is null ? "" : $"&continuationToken={Uri.EscapeDataString(continuationToken)}");

            var url =
                $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryIdOrName)}/refs?{qs}";

            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            http.DefaultRequestHeaders.Authorization = CreatePatAuthHeader(pat);

            using var resp = await http.GetAsync(url, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Azure DevOps list refs failed ({(int)resp.StatusCode}): {text}");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                        ? nameEl.GetString() ?? ""
                        : "";
                    var objectId = item.TryGetProperty("objectId", out var oidEl) && oidEl.ValueKind == JsonValueKind.String
                        ? oidEl.GetString() ?? ""
                        : "";
                    if (name.Length > 0 && objectId.Length > 0)
                        results.Add(new AzureDevOpsGitRef(name, objectId));
                }
            }

            continuationToken = root.TryGetProperty("continuationToken", out var ctEl) && ctEl.ValueKind == JsonValueKind.String
                ? ctEl.GetString()
                : null;
        }
        while (!string.IsNullOrEmpty(continuationToken));

        return results;
    }

    public async Task<DateTimeOffset?> TryGetCommitDateAsync(
        Guid userId,
        string organization,
        string project,
        string repositoryIdOrName,
        string commitId,
        CancellationToken ct)
    {
        var pat = await _patResolver.ResolvePatForOrganizationAsync(userId, organization, ct);

        var url =
            $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryIdOrName)}/commits/{Uri.EscapeDataString(commitId)}?api-version={Uri.EscapeDataString(_options.ApiVersion)}";

        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Authorization = CreatePatAuthHeader(pat);

        using var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Azure DevOps get commit failed ({(int)resp.StatusCode}): {text}");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.TryGetProperty("committer", out var committer) &&
            committer.TryGetProperty("date", out var dateEl) &&
            dateEl.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(dateEl.GetString(), out var dto))
            return dto;

        if (root.TryGetProperty("author", out var author) &&
            author.TryGetProperty("date", out var authorDateEl) &&
            authorDateEl.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(authorDateEl.GetString(), out var authorDto))
            return authorDto;

        return null;
    }

    public async Task DeleteBranchRefAsync(
        Guid userId,
        string organization,
        string project,
        string repositoryIdOrName,
        string refName,
        string oldObjectId,
        CancellationToken ct)
    {
        var pat = await _patResolver.ResolvePatForOrganizationAsync(userId, organization, ct);
        refName = NormalizeRefName(refName);

        var url =
            $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryIdOrName)}/refs?api-version={Uri.EscapeDataString(_options.ApiVersion)}";

        var body = new[]
        {
            new
            {
                name = refName,
                oldObjectId,
                newObjectId = "0000000000000000000000000000000000000000"
            }
        };

        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Authorization = CreatePatAuthHeader(pat);

        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync(url, content, ct);
        var respText = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Azure DevOps delete branch failed ({(int)resp.StatusCode}): {respText}");
    }

    public static string BranchShortName(string refName)
    {
        refName = refName.Trim();
        const string prefix = "refs/heads/";
        if (refName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return refName[prefix.Length..];
        if (refName.StartsWith("heads/", StringComparison.OrdinalIgnoreCase))
            return refName["heads/".Length..];
        return refName;
    }

    private static string NormalizeRefName(string refName)
    {
        refName = refName.Trim();
        if (refName.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase))
            return refName;
        if (refName.StartsWith("heads/", StringComparison.OrdinalIgnoreCase))
            return "refs/" + refName;
        if (refName.StartsWith("refs/", StringComparison.OrdinalIgnoreCase))
            return refName;
        return $"refs/heads/{refName}";
    }
}

public sealed record AzureDevOpsCreatePullRequestResponse
{
    public int PullRequestId { get; init; }

    // Not part of ADO response; we fill it in.
    public string Url { get; init; } = "";
}

public sealed record AzureDevOpsCommitBrief(string CommitId, string Comment, string AuthorName, DateTimeOffset CommittedDate);

public sealed record AzureDevOpsGitRef(string Name, string ObjectId);
