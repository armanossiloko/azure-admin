using AzureAdmin.API.Configuration;
using AzureAdmin.API.Contracts;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Releases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AzureAdmin.API.Tests;

public sealed class PullRequestStatusSyncServiceTests
{
    [Fact]
    public async Task One_failed_release_does_not_stop_the_rest_of_the_run()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        Guid brokenId;
        Guid okId;
        await using (var db = new ApplicationDbContext(options))
        {
            brokenId = await SeedReleaseWithActivePrAsync(db, "Broken");
            okId = await SeedReleaseWithActivePrAsync(db, "Ok");
        }

        var refresher = new RefreshStub { FailFor = brokenId };
        await using var provider = BuildProvider(options, refresher);
        var sync = new PullRequestStatusSyncService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptionsMonitor<PullRequestSyncOptions>>(),
            NullLogger<PullRequestStatusSyncService>.Instance);

        var summary = await sync.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, refresher.Called.Count);
        Assert.Contains(brokenId, refresher.Called);
        Assert.Contains(okId, refresher.Called);
        Assert.Equal(1, summary.ReleasesFailed);
        Assert.Equal(1, summary.ReleasesScanned);
    }

    private static ServiceProvider BuildProvider(
        DbContextOptions<ApplicationDbContext> dbOptions,
        IPullRequestStatusRefresher refresher)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbOptions);
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        services.AddSingleton(refresher);
        services.AddSingleton<IOptionsMonitor<PullRequestSyncOptions>>(new StaticOptions());
        return services.BuildServiceProvider();
    }

    private static async Task<Guid> SeedReleaseWithActivePrAsync(ApplicationDbContext db, string title)
    {
        var team = new Team { Id = Guid.NewGuid(), Name = title + " team" };
        var repo = new RegisteredRepository
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            AzureDevOpsOrganization = "contoso",
            AzureDevOpsProject = "proj",
            RepositoryIdOrName = title.ToLowerInvariant()
        };
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Title = title,
            Status = ReleaseLifecycleStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ReleasePullRequests.Add(new ReleasePullRequest
        {
            Id = Guid.NewGuid(),
            ReleaseId = release.Id,
            Release = release,
            TeamId = team.Id,
            Team = team,
            RegisteredRepositoryId = repo.Id,
            RegisteredRepository = repo,
            Phase = ReleasePrPhase.DevToMaster,
            Status = ReleasePullRequestStatus.Active,
            AzureDevOpsPullRequestId = 1,
            Url = "https://dev.azure.com/contoso/proj/_git/repo/pullrequest/1",
            SourceRefName = "refs/heads/dev",
            TargetRefName = "refs/heads/master",
            Title = title + " PR",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return release.Id;
    }

    private sealed class RefreshStub : IPullRequestStatusRefresher
    {
        public Guid FailFor { get; init; }
        public List<Guid> Called { get; } = [];

        public Task<IReadOnlyList<ReleasePullRequestStatusResult>> RefreshPullRequestStatusesAsync(
            Guid releaseId,
            ReleasePrPhase? phase,
            bool onlyActive,
            Func<ReleasePullRequest, Guid?> userFor,
            Guid? fallbackNotificationRecipient,
            int maxConcurrency,
            CancellationToken cancellationToken)
        {
            Called.Add(releaseId);
            if (releaseId == FailFor)
                throw new InvalidOperationException("simulated failure for one release");

            return Task.FromResult<IReadOnlyList<ReleasePullRequestStatusResult>>([]);
        }
    }

    private sealed class StaticOptions : IOptionsMonitor<PullRequestSyncOptions>
    {
        public PullRequestSyncOptions CurrentValue { get; } = new();
        public PullRequestSyncOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PullRequestSyncOptions, string?> listener) => null;
    }
}
