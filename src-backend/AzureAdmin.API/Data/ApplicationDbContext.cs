using AzureAdmin.API.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Data;

public sealed class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<RegisteredRepository> RegisteredRepositories => Set<RegisteredRepository>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<ReleaseTeam> ReleaseTeams => Set<ReleaseTeam>();
    public DbSet<ReleasePullRequest> ReleasePullRequests => Set<ReleasePullRequest>();
    public DbSet<ReleaseRepositoryCommitNotes> ReleaseRepositoryCommitNotes => Set<ReleaseRepositoryCommitNotes>();
    public DbSet<AzureDevOpsPatCredential> AzureDevOpsPatCredentials => Set<AzureDevOpsPatCredential>();
    public DbSet<UserAzureDevOpsOrganization> UserAzureDevOpsOrganizations => Set<UserAzureDevOpsOrganization>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<AdminActionLog> AdminActionLogs => Set<AdminActionLog>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(256);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentTeamId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RegisteredRepository>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AzureDevOpsOrganization).HasMaxLength(256).IsRequired();
            e.Property(x => x.AzureDevOpsProject).HasMaxLength(256).IsRequired();
            e.Property(x => x.RepositoryIdOrName).HasMaxLength(512).IsRequired();
            e.Property(x => x.ServiceName).HasMaxLength(512);
            e.Property(x => x.IsDecommissioned).HasDefaultValue(false);
            e.HasIndex(x => new { x.AzureDevOpsOrganization, x.AzureDevOpsProject, x.RepositoryIdOrName })
                .IsUnique();
            e.HasOne(x => x.Team)
                .WithMany(x => x.RegisteredRepositories)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Release>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(512).IsRequired();
            e.Property(x => x.SprintLabel).HasMaxLength(128);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            e.HasOne(x => x.CreatedBy)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReleaseTeam>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ReleaseId, x.TeamId }).IsUnique();
            e.HasOne(x => x.Release)
                .WithMany(x => x.Teams)
                .HasForeignKey(x => x.ReleaseId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Team)
                .WithMany(x => x.ReleaseTeams)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AzureDevOpsPatCredential>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OrganizationKey).HasMaxLength(256).IsRequired();
            e.Property(x => x.OrganizationDisplay).HasMaxLength(256).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.PatExpiresAt);
            e.HasIndex(x => new { x.UserId, x.OrganizationKey }).IsUnique();
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserAzureDevOpsOrganization>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OrganizationKey).HasMaxLength(256).IsRequired();
            e.Property(x => x.OrganizationDisplay).HasMaxLength(256).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => new { x.UserId, x.OrganizationKey }).IsUnique();
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReleasePullRequest>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Url).HasMaxLength(2048).IsRequired();
            e.Property(x => x.SourceRefName).HasMaxLength(512).IsRequired();
            e.Property(x => x.TargetRefName).HasMaxLength(512).IsRequired();
            e.Property(x => x.Title).HasMaxLength(1024).IsRequired();
            e.HasIndex(x => new { x.ReleaseId, x.RegisteredRepositoryId, x.Phase }).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.Release)
                .WithMany(x => x.PullRequests)
                .HasForeignKey(x => x.ReleaseId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Team)
                .WithMany(x => x.ReleasePullRequests)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RegisteredRepository)
                .WithMany(x => x.ReleasePullRequests)
                .HasForeignKey(x => x.RegisteredRepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReleaseRepositoryCommitNotes>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceRefName).HasMaxLength(512).IsRequired();
            e.Property(x => x.TargetRefName).HasMaxLength(512).IsRequired();
            e.Property(x => x.CommitsJson).IsRequired();
            e.HasIndex(x => new { x.ReleaseId, x.RegisteredRepositoryId, x.Phase }).IsUnique();
            e.HasOne(x => x.Release)
                .WithMany(x => x.RepositoryCommitNotes)
                .HasForeignKey(x => x.ReleaseId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RegisteredRepository)
                .WithMany(x => x.ReleaseRepositoryCommitNotes)
                .HasForeignKey(x => x.RegisteredRepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.JiraBaseUrl).HasMaxLength(512);
            e.Property(x => x.JiraProjectKey).HasMaxLength(512);
            e.Property(x => x.BrandingApplicationName).HasMaxLength(128);
            e.Property(x => x.BrandingTagline).HasMaxLength(256);
            e.Property(x => x.BrandingDescription).HasMaxLength(1024);
        });

        modelBuilder.Entity<UserNotification>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DedupeKey).HasMaxLength(256).IsRequired();
            e.Property(x => x.Kind).HasMaxLength(64).IsRequired();
            e.Property(x => x.Title).HasMaxLength(512).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000);
            e.Property(x => x.Href).HasMaxLength(512);
            e.HasIndex(x => new { x.UserId, x.DedupeKey }).IsUnique();
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPreferences>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.PreferredTheme).HasMaxLength(16);
            e.HasOne(x => x.User)
                .WithOne()
                .HasForeignKey<UserPreferences>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AdminActionLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(64).IsRequired();
            e.Property(x => x.TargetType).HasMaxLength(64).IsRequired();
            e.Property(x => x.TargetKey).HasMaxLength(1024).IsRequired();
            e.Property(x => x.ErrorMessage).HasMaxLength(2000);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Action);
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WebhookSubscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.UrlPreview).HasMaxLength(512).IsRequired();
            e.Property(x => x.TemplateCreated).HasMaxLength(4000);
            e.Property(x => x.TemplateMerged).HasMaxLength(4000);
            e.Property(x => x.LastDeliveryError).HasMaxLength(2000);
            e.HasIndex(x => x.Enabled);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampReleases();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampReleases();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Every write to a release gets a fresh concurrency stamp, and edits also record <see cref="Release.UpdatedAt"/>.
    /// Doing it here covers all writers (controller edits, the batch service promoting a release to Active, ...).
    /// The stamp's original value stays whatever was loaded (or what the client sent), so EF's UPDATE ... WHERE
    /// still detects a concurrent change.
    /// </summary>
    private void StampReleases()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Release>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.ConcurrencyStamp ??= Guid.NewGuid();
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.ConcurrencyStamp = Guid.NewGuid();
                    break;
            }
        }
    }
}
