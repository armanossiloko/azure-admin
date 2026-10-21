using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Tests;

public sealed class ReleaseConcurrencyStampTests
{
    private static DbContextOptions<ApplicationDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static async Task<Guid> SeedReleaseAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        var release = new Release
        {
            Id = Guid.NewGuid(),
            Title = "Release sprint 2026/42",
            Status = ReleaseLifecycleStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Releases.Add(release);
        await db.SaveChangesAsync();
        return release.Id;
    }

    [Fact]
    public async Task New_release_gets_a_stamp_and_no_update_time()
    {
        var options = NewDatabase();
        var id = await SeedReleaseAsync(options);

        await using var db = new ApplicationDbContext(options);
        var release = await db.Releases.SingleAsync(r => r.Id == id);

        Assert.NotNull(release.ConcurrencyStamp);
        Assert.Null(release.UpdatedAt);
    }

    [Fact]
    public async Task Editing_a_release_records_the_time_and_rotates_the_stamp()
    {
        var options = NewDatabase();
        var id = await SeedReleaseAsync(options);

        await using var db = new ApplicationDbContext(options);
        var release = await db.Releases.SingleAsync(r => r.Id == id);
        var before = release.ConcurrencyStamp;

        release.Title = "Renamed";
        await db.SaveChangesAsync();

        Assert.NotNull(release.UpdatedAt);
        Assert.NotNull(release.ConcurrencyStamp);
        Assert.NotEqual(before, release.ConcurrencyStamp);
    }

    [Fact]
    public async Task Saving_without_changes_keeps_the_stamp()
    {
        var options = NewDatabase();
        var id = await SeedReleaseAsync(options);

        await using var db = new ApplicationDbContext(options);
        var release = await db.Releases.SingleAsync(r => r.Id == id);
        var before = release.ConcurrencyStamp;

        await db.SaveChangesAsync();

        Assert.Equal(before, release.ConcurrencyStamp);
        Assert.Null(release.UpdatedAt);
    }

    [Fact]
    public async Task A_write_based_on_a_stale_read_is_rejected()
    {
        var options = NewDatabase();
        var id = await SeedReleaseAsync(options);

        // Two users open the same release.
        await using var first = new ApplicationDbContext(options);
        await using var second = new ApplicationDbContext(options);
        var mine = await first.Releases.SingleAsync(r => r.Id == id);
        var theirs = await second.Releases.SingleAsync(r => r.Id == id);

        theirs.Status = ReleaseLifecycleStatus.Active;
        await second.SaveChangesAsync();

        mine.Title = "My edit";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync());
    }

    [Fact]
    public async Task A_client_supplied_stale_stamp_is_rejected()
    {
        var options = NewDatabase();
        var id = await SeedReleaseAsync(options);

        Guid? stampTheClientSaw;
        await using (var read = new ApplicationDbContext(options))
            stampTheClientSaw = (await read.Releases.SingleAsync(r => r.Id == id)).ConcurrencyStamp;

        await using (var other = new ApplicationDbContext(options))
        {
            (await other.Releases.SingleAsync(r => r.Id == id)).SprintLabel = "2026/43";
            await other.SaveChangesAsync();
        }

        // Same pattern as ReleasesController.Update: compare against the stamp the client sent.
        await using var db = new ApplicationDbContext(options);
        var release = await db.Releases.SingleAsync(r => r.Id == id);
        db.Entry(release).Property(r => r.ConcurrencyStamp).OriginalValue = stampTheClientSaw;
        release.Title = "Stale edit";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }
}
