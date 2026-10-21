using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AzureAdmin.API.Configuration;
using Microsoft.Extensions.Options;

namespace AzureAdmin.API.Services.AzureDevOps;

public sealed class AzureDevOpsClient
{
    /// <summary>
    /// Named client for idempotent reads (GET). Wrapped in the standard resilience pipeline:
    /// retries with backoff on 5xx/408/429 (honouring <c>Retry-After</c>), per-attempt and total timeouts,
    /// and a circuit breaker. Safe to retry because a repeated GET has no side effects.
    /// </summary>
    public const string ReadHttpClientName = "AzureDevOps.Read";

    /// <summary>
    /// Named client for writes (create PR, complete PR, delete ref). Only retried on 429/503, where
    /// Azure DevOps guarantees the request was not processed; a blind retry on other failures could
    /// open a duplicate pull request.
    /// </summary>
    public const string WriteHttpClientName = "AzureDevOps.Write";

    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureDevOpsOptions _options;
    private readonly IAzureDevOpsPatResolver _patResolver;
    private readonly ILogger<AzureDevOpsClient> _logger;

    public AzureDevOpsClient(
        IHttpClientFactory httpClientFactory,
        IOptions<AzureDevOpsOptions> options,
        IAzureDevOpsPatResolver patResolver,
        ILogger<AzureDevOpsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _patResolver = patResolver;
        _logger = logger;
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

        var url = RepoApiUrl(organization, project, repositoryIdOrName, "pullrequests");

        var body = new
        {
            sourceRefName,
            targetRefName,
            title,
            description
        };

        using var resp = await SendAsync(WriteHttpClientName, HttpMethod.Post, url, pat, body, ct);
        var respText = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw AzureDevOpsHttp.Failure("PR create", resp, respText, _logger);

        var parsed = JsonSerializer.Deserialize<AzureDevOpsCreatePullRequestResponse>(respText, CaseInsensitive);

        if (parsed is null)
            throw new InvalidOperationException("Azure DevOps response could not be parsed.");

        // The response does not include a browser URL; construct one.
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
        return await TryGetGitPullRequestStatusAsync(pat, organization, project, repositoryIdOrName, pullRequestId, ct);
    }

    /// <summary>
    /// Same as the <see cref="Guid"/> overload but takes an already-resolved PAT, for callers that fan out
    /// many lookups concurrently and must not hit the PAT resolver (and its DbContext) in parallel.
    /// </summary>
    public async Task<string?> TryGetGitPullRequestStatusAsync(
        string pat,
        string organization,
        string project,
        string repositoryIdOrName,
        int pullRequestId,
        CancellationToken ct)
    {
        var url = RepoApiUrl(organization, project, repositoryIdOrName, $"pullrequests/{pullRequestId}");

        using var resp = await SendAsync(ReadHttpClientName, HttpMethod.Get, url, pat, body: null, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw AzureDevOpsHttp.Failure("get PR", resp, text, _logger);

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

        var url = RepoApiUrl(organization, project, repositoryIdOrName, $"pullrequests/{pullRequestId}/commits", $"$top={top}");

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
            $"$top={top}" +
            $"&searchCriteria.itemVersion.version={Uri.EscapeDataString(itemBranch)}" +
            "&searchCriteria.itemVersion.versionType=branch" +
            $"&searchCriteria.compareVersion.version={Uri.EscapeDataString(compareBranch)}" +
            "&searchCriteria.compareVersion.versionType=branch";

        var url = RepoApiUrl(organization, project, repositoryIdOrName, "commits", qs);

        return await GetCommitListFromUrlAsync(pat, url, ct);
    }

    private async Task<IReadOnlyList<AzureDevOpsCommitBrief>> GetCommitListFromUrlAsync(
        string pat,
        string url,
        CancellationToken ct)
    {
        using var resp = await SendAsync(ReadHttpClientName, HttpMethod.Get, url, pat, body: null, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw AzureDevOpsHttp.Failure("commits", resp, text, _logger);

        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<AzureDevOpsCommitBrief>();

        return GitCommitJsonParser.ParseCommitArray(arr);
    }

    /// <summary>Fetches PR status, merge status and the source commit needed to complete the PR.</summary>
    public async Task<AzureDevOpsPullRequestSnapshot?> TryGetPullRequestSnapshotAsync(
        Guid userId,
        string organization,
        string project,
        string repositoryIdOrName,
        int pullRequestId,
        CancellationToken ct)
    {
        var pat = await _patResolver.ResolvePatForOrganizationAsync(userId, organization, ct);

        var url = RepoApiUrl(organization, project, repositoryIdOrName, $"pullrequests/{pullRequestId}");

        using var resp = await SendAsync(ReadHttpClientName, HttpMethod.Get, url, pat, body: null, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw AzureDevOpsHttp.Failure("get PR", resp, text, _logger);

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        var status = root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String
            ? statusEl.GetString() ?? ""
            : "";

        var mergeStatus = root.TryGetProperty("mergeStatus", out var mergeStatusEl) && mergeStatusEl.ValueKind == JsonValueKind.String
            ? mergeStatusEl.GetString()
            : null;

        string? commitId = null;
        if (root.TryGetProperty("lastMergeSourceCommit", out var lastMergeSourceCommit) &&
            lastMergeSourceCommit.TryGetProperty("commitId", out var commitIdEl) &&
            commitIdEl.ValueKind == JsonValueKind.String)
            commitId = commitIdEl.GetString();

        return new AzureDevOpsPullRequestSnapshot(status, mergeStatus, commitId);
    }

    /// <summary>Completes a pull request using the "rebase and fast-forward" merge strategy (no merge commit).</summary>
    public async Task CompletePullRequestAsync(
        Guid userId,
        string organization,
        string project,
        string repositoryIdOrName,
        int pullRequestId,
        string lastMergeSourceCommitId,
        CancellationToken ct)
    {
        var pat = await _patResolver.ResolvePatForOrganizationAsync(userId, organization, ct);

        var url = RepoApiUrl(organization, project, repositoryIdOrName, $"pullrequests/{pullRequestId}");

        var body = new
        {
            status = "completed",
            lastMergeSourceCommit = new { commitId = lastMergeSourceCommitId },
            completionOptions = new
            {
                mergeStrategy = "rebase",
                deleteSourceBranch = false,
                bypassPolicy = false
            }
        };

        using var resp = await SendAsync(WriteHttpClientName, HttpMethod.Patch, url, pat, body, ct);
        var respText = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw AzureDevOpsHttp.Failure("PR complete", resp, respText, _logger);
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
                "filter=heads/" +
                (continuationToken is null ? "" : $"&continuationToken={Uri.EscapeDataString(continuationToken)}");

            var url = RepoApiUrl(organization, project, repositoryIdOrName, "refs", qs);

            using var resp = await SendAsync(ReadHttpClientName, HttpMethod.Get, url, pat, body: null, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw AzureDevOpsHttp.Failure("list refs", resp, text, _logger);

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

            // Azure DevOps returns the continuation token in a response header, not the JSON body.
            continuationToken = resp.Headers.TryGetValues("x-ms-continuationtoken", out var ctValues)
                ? ctValues.FirstOrDefault()
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
        return await TryGetCommitDateAsync(pat, organization, project, repositoryIdOrName, commitId, ct);
    }

    /// <summary>
    /// Same as the <see cref="Guid"/> overload but takes an already-resolved PAT. Use this when fanning out
    /// many concurrent lookups (e.g. per-branch commit dates) so callers resolve the PAT once instead of each
    /// parallel call hitting <see cref="IAzureDevOpsPatResolver"/> (and its backing DbContext) concurrently.
    /// </summary>
    public async Task<DateTimeOffset?> TryGetCommitDateAsync(
        string pat,
        string organization,
        string project,
        string repositoryIdOrName,
        string commitId,
        CancellationToken ct)
    {
        var url = RepoApiUrl(organization, project, repositoryIdOrName, $"commits/{Uri.EscapeDataString(commitId)}");

        using var resp = await SendAsync(ReadHttpClientName, HttpMethod.Get, url, pat, body: null, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;

        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw AzureDevOpsHttp.Failure("get commit", resp, text, _logger);

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

        var url = RepoApiUrl(organization, project, repositoryIdOrName, "refs");

        var body = new[]
        {
            new
            {
                name = refName,
                oldObjectId,
                newObjectId = "0000000000000000000000000000000000000000"
            }
        };

        using var resp = await SendAsync(WriteHttpClientName, HttpMethod.Post, url, pat, body, ct);
        var respText = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw AzureDevOpsHttp.Failure("delete branch", resp, respText, _logger);
    }

    /// <summary>
    /// Sends one request through the named client. Auth and Accept headers are set per request (not on the
    /// client) so the pooled handler and its resilience pipeline are shared across users. Timeouts and an open
    /// circuit come back as <see cref="HttpRequestException"/> (see <see cref="AzureDevOpsHttp.SendAsync"/>).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        string clientName,
        HttpMethod method,
        string url,
        string pat,
        object? body,
        CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient(clientName);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = CreatePatAuthHeader(pat);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        return await AzureDevOpsHttp.SendAsync(http, request, ct);
    }

    private string RepoApiUrl(string organization, string project, string repositoryIdOrName, string path, string? query = null)
    {
        var url =
            $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repositoryIdOrName)}/{path}" +
            $"?api-version={Uri.EscapeDataString(_options.ApiVersion)}";
        return string.IsNullOrEmpty(query) ? url : $"{url}&{query}";
    }

    internal static AuthenticationHeaderValue CreatePatAuthHeader(string pat)
    {
        // Basic auth: username can be empty; PAT is the password.
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        return new AuthenticationHeaderValue("Basic", token);
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

public sealed record AzureDevOpsPullRequestSnapshot(string Status, string? MergeStatus, string? LastMergeSourceCommitId);
