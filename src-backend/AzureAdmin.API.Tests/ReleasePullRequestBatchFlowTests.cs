using System.Net;
using System.Text;
using System.Text.Json;
using AzureAdmin.API.Configuration;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.AzureDevOps;
using AzureAdmin.API.Services.Identity;
using AzureAdmin.API.Services.Notifications;
using AzureAdmin.API.Services.Releases;
using AzureAdmin.API.Services.Webhooks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AzureAdmin.API.Tests;

/// <summary>
/// Runs the release batch flow against a fake Azure DevOps that answers the commits query the way the
/// real service does: <c>itemVersion=A&amp;compareVersion=B</c> returns the commits in B that are not in A
/// (measured against dev.azure.com; the docs do not say which way round it is).
/// </summary>
public sealed class ReleasePullRequestBatchFlowTests
{
    [Fact]
    public async Task Commits_between_branches_returns_source_commits_the_target_is_missing()
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base", "feature");
        flow.Ado.Branch("master", "base");

        var ahead = await flow.Client.GetCommitsBetweenBranchesAsync(
            flow.UserId, "org", "proj", "repo", "dev", "master", 10, CancellationToken.None);
        var behind = await flow.Client.GetCommitsBetweenBranchesAsync(
            flow.UserId, "org", "proj", "repo", "master", "dev", 10, CancellationToken.None);

        Assert.Equal("feature", Assert.Single(ahead).CommitId);
        Assert.Empty(behind);
    }

    [Fact]
    public async Task Dev_to_master_with_dev_ahead_opens_only_the_promotion_without_a_note()
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base", "feature");
        flow.Ado.Branch("master", "base");
        flow.Ado.Branch("prod", "base");

        var preflight = await flow.PreflightAsync(ReleasePrPhase.DevToMaster);
        var created = await flow.CreateAsync(ReleasePrPhase.DevToMaster);

        var repo = Assert.Single(preflight.Repositories);
        Assert.True(repo.Ok);
        Assert.Empty(repo.Notices ?? []);
        var pr = Assert.Single(created.Results);
        Assert.Equal(ReleasePrPhase.DevToMaster, pr.Phase);
        Assert.Null(pr.Note);
        Assert.Equal(("dev", "master"), Assert.Single(flow.Ado.Opened));
    }

    [Fact]
    public async Task Dev_to_master_with_prod_ahead_opens_the_back_merge_and_the_promotion()
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base", "feature");
        flow.Ado.Branch("master", "base");
        flow.Ado.Branch("prod", "base", "hotfix");

        var preflight = await flow.PreflightAsync(ReleasePrPhase.DevToMaster);
        var created = await flow.CreateAsync(ReleasePrPhase.DevToMaster);

        Assert.True(preflight.CanProceed);
        Assert.Equal(
            [ReleasePrPhase.ProdToDev, ReleasePrPhase.DevToMaster],
            created.Results.Select(r => r.Phase!.Value));
        Assert.NotNull(created.Results[0].Note);
        Assert.Null(created.Results[1].Note);
        Assert.Equal([("prod", "dev"), ("dev", "master")], flow.Ado.Opened);
    }

    [Fact]
    public async Task Prod_to_dev_skips_a_repository_that_is_in_sync_and_opens_the_one_that_is_not()
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base", "feature");
        flow.Ado.Branch("prod", "base", "hotfix");
        var inSync = await flow.AddRepositoryAsync("in-sync", serviceName: "In Sync Service");
        flow.Ado.RepoBranch("in-sync", "dev", "base", "feature");
        flow.Ado.RepoBranch("in-sync", "prod", "base");

        var preflight = await flow.PreflightAsync(ReleasePrPhase.ProdToDev, flow.RepoId, inSync);
        var created = await flow.CreateAsync(ReleasePrPhase.ProdToDev, flow.RepoId, inSync);

        Assert.True(preflight.CanProceed);
        Assert.Equal("repo", Assert.Single(created.Results).RepositoryIdOrName);
        Assert.Equal("In Sync Service", Assert.Single(created.AlreadyInSyncRepositories!));
    }

    [Fact]
    public async Task Prod_to_dev_with_everything_in_sync_succeeds_without_opening_anything()
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base");
        flow.Ado.Branch("prod", "base");

        var preflight = await flow.PreflightAsync(ReleasePrPhase.ProdToDev);
        var created = await flow.CreateAsync(ReleasePrPhase.ProdToDev);

        Assert.True(preflight.CanProceed);
        Assert.Empty(created.Results);
        Assert.Equal("repo", Assert.Single(created.AlreadyInSyncRepositories!));
        Assert.Empty(flow.Ado.Opened);
        Assert.Equal(ReleaseLifecycleStatus.Draft, await flow.ReleaseStatusAsync());
    }

    [Fact]
    public async Task Prod_to_dev_treats_a_repository_without_a_prod_branch_as_in_sync()
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base");

        var preflight = await flow.PreflightAsync(ReleasePrPhase.ProdToDev);
        var created = await flow.CreateAsync(ReleasePrPhase.ProdToDev);

        Assert.True(preflight.CanProceed);
        Assert.Empty(created.Results);
        Assert.Single(created.AlreadyInSyncRepositories!);
    }

    [Fact]
    public async Task Running_a_batch_again_reuses_the_open_pull_requests()
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base", "feature");
        flow.Ado.Branch("master", "base");
        flow.Ado.Branch("prod", "base", "hotfix");

        var first = await flow.CreateAsync(ReleasePrPhase.DevToMaster);
        var preflight = await flow.PreflightAsync(ReleasePrPhase.DevToMaster);
        var second = await flow.CreateAsync(ReleasePrPhase.DevToMaster);

        Assert.True(preflight.CanProceed);
        Assert.Equal(first.Results.Select(r => r.PullRequestId), second.Results.Select(r => r.PullRequestId));
        Assert.All(second.Results, r => Assert.Null(r.Id));
        Assert.Equal(2, flow.Ado.Opened.Count);
    }

    [Fact]
    public async Task A_pull_request_already_open_in_azure_devops_is_adopted()
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base");
        flow.Ado.Branch("prod", "base", "hotfix");
        var openedByHand = flow.Ado.OpenPullRequest("prod", "dev");

        var created = await flow.CreateAsync(ReleasePrPhase.ProdToDev);

        Assert.Equal(openedByHand, Assert.Single(created.Results).PullRequestId);
        Assert.Equal(openedByHand, await flow.StoredPullRequestIdAsync(ReleasePrPhase.ProdToDev));
    }

    [Fact]
    public async Task A_completed_back_merge_is_replaced_when_prod_has_gained_commits_since()
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base");
        flow.Ado.Branch("prod", "base", "hotfix");
        var first = Assert.Single((await flow.CreateAsync(ReleasePrPhase.ProdToDev)).Results);
        flow.Ado.Status[first.PullRequestId] = "completed";

        var second = Assert.Single((await flow.CreateAsync(ReleasePrPhase.ProdToDev)).Results);

        Assert.NotEqual(first.PullRequestId, second.PullRequestId);
        Assert.Equal(second.PullRequestId, await flow.StoredPullRequestIdAsync(ReleasePrPhase.ProdToDev));
    }

    [Fact]
    public async Task Master_to_prod_is_not_blocked_by_the_merge_commit_dev_to_master_left_on_master()
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base", "feature");
        flow.Ado.Branch("master", "base", "feature", "merge-commit");
        flow.Ado.Branch("prod", "base");
        await flow.SeedPullRequestAsync(ReleasePrPhase.DevToMaster, ReleasePullRequestStatus.Completed);

        var preflight = await flow.PreflightAsync(ReleasePrPhase.MasterToProd);
        var created = await flow.CreateAsync(ReleasePrPhase.MasterToProd);

        Assert.True(preflight.CanProceed);
        Assert.Equal(ReleasePrPhase.MasterToProd, Assert.Single(created.Results).Phase);
    }

    [Fact]
    public async Task Dev_to_master_is_still_blocked_when_only_master_has_commits_dev_is_missing()
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base");
        flow.Ado.Branch("master", "base", "master-only");
        flow.Ado.Branch("prod", "base");

        var preflight = await flow.PreflightAsync(ReleasePrPhase.DevToMaster);

        Assert.False(preflight.CanProceed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.CreateAsync(ReleasePrPhase.DevToMaster));
        Assert.Empty(flow.Ado.Opened);
    }

    [Theory]
    [InlineData(ReleasePrPhase.ProdToDev, "noFastForward")]
    [InlineData(ReleasePrPhase.DevToMaster, "rebase")]
    public async Task Complete_all_uses_a_merge_commit_only_for_the_back_merge(ReleasePrPhase phase, string strategy)
    {
        await using var flow = new Flow();
        flow.Ado.Branch("dev", "base", "feature");
        flow.Ado.Branch("master", "base");
        flow.Ado.Branch("prod", "base", "hotfix");
        await flow.CreateAsync(phase);

        var results = await flow.Service.CompletePullRequestsForPhaseAsync(flow.ReleaseId, phase, CancellationToken.None);

        Assert.True(Assert.Single(results).Success);
        Assert.Equal(strategy, Assert.Single(flow.Ado.MergeStrategies));
    }

    private sealed class Flow : IAsyncDisposable
    {
        private readonly ApplicationDbContext _db;

        public Guid UserId { get; } = Guid.NewGuid();
        public Guid ReleaseId { get; } = Guid.NewGuid();
        public Guid TeamId { get; } = Guid.NewGuid();
        public Guid RepoId { get; } = Guid.NewGuid();
        public FakeAzureDevOps Ado { get; } = new();
        public AzureDevOpsClient Client { get; }
        public ReleasePullRequestBatchService Service { get; }

        public Flow()
        {
            _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

            _db.Teams.Add(new Team { Id = TeamId, Name = "backend" });
            _db.Releases.Add(new Release
            {
                Id = ReleaseId,
                Title = "Release",
                SprintLabel = "2026/45",
                Status = ReleaseLifecycleStatus.Draft
            });
            _db.RegisteredRepositories.Add(new RegisteredRepository
            {
                Id = RepoId,
                TeamId = TeamId,
                AzureDevOpsOrganization = "org",
                AzureDevOpsProject = "proj",
                RepositoryIdOrName = "repo"
            });
            _db.SaveChanges();

            var factory = new FakeHttpClientFactory(Ado);
            Client = new AzureDevOpsClient(
                factory,
                Options.Create(new AzureDevOpsOptions { ApiVersion = "7.1" }),
                new FixedPatResolver(),
                NullLogger<AzureDevOpsClient>.Instance);

            var currentUser = new FixedCurrentUser(UserId);
            Service = new ReleasePullRequestBatchService(
                _db,
                Client,
                currentUser,
                new ReleaseCommitNotesService(_db, Client, NullLogger<ReleaseCommitNotesService>.Instance),
                new NotificationService(_db, currentUser),
                new OutboundWebhookPublisher(
                    _db,
                    new WebhookSubscriptionService(_db, new EphemeralDataProtectionProvider()),
                    factory,
                    new HttpContextAccessor(),
                    new StaticOptionsMonitor<AppOptions>(new AppOptions()),
                    NullLogger<OutboundWebhookPublisher>.Instance),
                new FixedPatResolver(),
                NullLogger<ReleasePullRequestBatchService>.Instance);
        }

        public async Task<Guid> AddRepositoryAsync(string name, string? serviceName = null)
        {
            var id = Guid.NewGuid();
            _db.RegisteredRepositories.Add(new RegisteredRepository
            {
                Id = id,
                TeamId = TeamId,
                AzureDevOpsOrganization = "org",
                AzureDevOpsProject = "proj",
                RepositoryIdOrName = name,
                ServiceName = serviceName
            });
            await _db.SaveChangesAsync();
            return id;
        }

        public async Task SeedPullRequestAsync(ReleasePrPhase phase, ReleasePullRequestStatus status)
        {
            _db.ReleasePullRequests.Add(new ReleasePullRequest
            {
                Id = Guid.NewGuid(),
                ReleaseId = ReleaseId,
                TeamId = TeamId,
                RegisteredRepositoryId = RepoId,
                Phase = phase,
                Status = status,
                AzureDevOpsPullRequestId = 1,
                Url = "https://example.test/pr/1",
                SourceRefName = "dev",
                TargetRefName = "master",
                Title = "seeded",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        public Task<BatchPreflightResponse> PreflightAsync(ReleasePrPhase phase, params Guid[] repoIds) =>
            Service.PreflightPullRequestsForReleaseAsync(ReleaseId, TeamId, Request(phase, repoIds), CancellationToken.None);

        public Task<BatchCreateReleasePullRequestsResponse> CreateAsync(ReleasePrPhase phase, params Guid[] repoIds) =>
            Service.CreatePullRequestsForReleaseAsync(ReleaseId, TeamId, Request(phase, repoIds), CancellationToken.None);

        public async Task<ReleaseLifecycleStatus> ReleaseStatusAsync() =>
            (await _db.Releases.AsNoTracking().SingleAsync(r => r.Id == ReleaseId)).Status;

        public async Task<int> StoredPullRequestIdAsync(ReleasePrPhase phase) =>
            (await _db.ReleasePullRequests.AsNoTracking()
                .SingleAsync(pr => pr.ReleaseId == ReleaseId && pr.Phase == phase)).AzureDevOpsPullRequestId;

        private BatchCreateReleasePullRequestsRequest Request(ReleasePrPhase phase, Guid[] repoIds) =>
            new(phase, "ignored", null, null, null, repoIds.Length > 0 ? repoIds : [RepoId]);

        public ValueTask DisposeAsync() => _db.DisposeAsync();
    }

    private sealed class FakeAzureDevOps : HttpMessageHandler
    {
        private readonly Dictionary<(string Repo, string Branch), HashSet<string>> _branches = new();
        private readonly List<(int Id, string Source, string Target)> _pullRequests = new();
        private int _nextId = 15904;

        public Dictionary<int, string> Status { get; } = new();
        public List<(string Source, string Target)> Opened { get; } = new();
        public List<string> MergeStrategies { get; } = new();

        public void Branch(string branch, params string[] commits) => RepoBranch("repo", branch, commits);

        public void RepoBranch(string repo, string branch, params string[] commits) =>
            _branches[(repo, branch)] = commits.ToHashSet();

        public int OpenPullRequest(string source, string target)
        {
            var id = _nextId++;
            _pullRequests.Add((id, source, target));
            Status[id] = "active";
            return id;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var repo = path.Split("/repositories/")[1].Split('/')[0];

            if (request.Method == HttpMethod.Get && path.EndsWith("/refs"))
            {
                var refs = _branches.Keys.Where(k => k.Repo == repo)
                    .Select(k => new { name = $"refs/heads/{k.Branch}", objectId = "abc" });
                return Json(new { value = refs });
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/commits") && !path.Contains("/pullrequests/"))
            {
                var item = query["searchCriteria.itemVersion.version"]!;
                var compare = query["searchCriteria.compareVersion.version"]!;
                if (!_branches.TryGetValue((repo, item), out var itemCommits)
                    || !_branches.TryGetValue((repo, compare), out var compareCommits))
                {
                    return Error(HttpStatusCode.NotFound, "The branch could not be resolved.", "GitUnresolvableToCommitException");
                }

                return Json(new { value = compareCommits.Except(itemCommits).Select(CommitJson) });
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/commits"))
                return Json(new { value = Array.Empty<object>() });

            if (request.Method == HttpMethod.Post && path.EndsWith("/pullrequests"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var source = body.RootElement.GetProperty("sourceRefName").GetString()!["refs/heads/".Length..];
                var target = body.RootElement.GetProperty("targetRefName").GetString()!["refs/heads/".Length..];
                if (_pullRequests.Any(p => p.Source == source && p.Target == target && Status[p.Id] == "active"))
                {
                    return Error(
                        HttpStatusCode.Conflict,
                        "TF401179: An active pull request for the source and target branch already exists.",
                        "GitPullRequestExistsException");
                }

                var id = OpenPullRequest(source, target);
                Opened.Add((source, target));
                return Json(new { pullRequestId = id });
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/pullrequests"))
            {
                var source = query["searchCriteria.sourceRefName"]!["refs/heads/".Length..];
                var target = query["searchCriteria.targetRefName"]!["refs/heads/".Length..];
                var matches = _pullRequests
                    .Where(p => p.Source == source && p.Target == target && Status[p.Id] == "active")
                    .Select(p => new { pullRequestId = p.Id });
                return Json(new { value = matches });
            }

            var pullRequestId = int.Parse(path.Split("/pullrequests/")[1].Split('/')[0]);
            if (request.Method == HttpMethod.Get)
            {
                return Json(new
                {
                    status = Status[pullRequestId],
                    mergeStatus = "succeeded",
                    lastMergeSourceCommit = new { commitId = "tip" }
                });
            }

            using (var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)))
            {
                MergeStrategies.Add(body.RootElement.GetProperty("completionOptions").GetProperty("mergeStrategy").GetString()!);
            }

            Status[pullRequestId] = "completed";
            return Json(new { });
        }

        private static object CommitJson(string id) => new
        {
            commitId = id,
            comment = id,
            author = new { name = "a", date = "2026-11-06T00:00:00Z" }
        };

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };

        private static HttpResponseMessage Error(HttpStatusCode status, string message, string typeKey) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { message, typeKey }), Encoding.UTF8, "application/json")
        };
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedPatResolver : IAzureDevOpsPatResolver
    {
        public Task<string> ResolvePatForOrganizationAsync(Guid userId, string organization, CancellationToken cancellationToken) =>
            Task.FromResult("pat");
    }

    private sealed class FixedCurrentUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public Guid GetRequiredUserId() => id;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
