using System.Text.Json;
using AzureAdmin.API.Contracts.Git;
using AzureAdmin.API.Data;
using AzureAdmin.API.Models;
using AzureAdmin.API.Services.AzureDevOps;
using AzureAdmin.API.Services.Identity;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Services.Git;

public sealed class StaleBranchService
{
    private static readonly HashSet<string> ProtectedBranchNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "main", "master", "dev", "develop", "prod", "production", "staging", "release"
    };

    private const int DefaultStaleDays = 90;
    private const int MaxConcurrentCommitLookups = 8;

    private readonly ApplicationDbContext _db;
    private readonly AzureDevOpsClient _ado;
    private readonly ICurrentUser _currentUser;

    public StaleBranchService(ApplicationDbContext db, AzureDevOpsClient ado, ICurrentUser currentUser)
    {
        _db = db;
        _ado = ado;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<GitBranchDto>> ListBranchesAsync(
        Guid? organizationId,
        Guid? registeredRepositoryId,
        int? staleDays,
        bool staleOnly,
        CancellationToken ct)
    {
        var userId = _currentUser.GetRequiredUserId();
        var repos = await LoadRegisteredRepositoriesAsync(userId, organizationId, registeredRepositoryId, ct);
        var thresholdDays = staleDays is > 0 ? staleDays.Value : DefaultStaleDays;
        var now = DateTimeOffset.UtcNow;
        var results = new List<GitBranchDto>();

        foreach (var repo in repos)
        {
            var orgKey = AzureDevOpsOrganizationService.NormalizeKey(repo.AzureDevOpsOrganization);
            IReadOnlyList<AzureDevOpsGitRef> refs;
            try
            {
                refs = await _ado.ListBranchRefsAsync(
                    userId,
                    orgKey,
                    repo.AzureDevOpsProject,
                    repo.RepositoryIdOrName,
                    ct);
            }
            catch (HttpRequestException)
            {
                continue;
            }

            using var gate = new SemaphoreSlim(MaxConcurrentCommitLookups);
            var branchTasks = refs.Select(async gitRef =>
            {
                var branchName = AzureDevOpsClient.BranchShortName(gitRef.Name);
                var isProtected = IsProtectedBranch(branchName);
                DateTimeOffset? lastCommit = null;

                if (!isProtected)
                {
                    await gate.WaitAsync(ct);
                    try
                    {
                        lastCommit = await _ado.TryGetCommitDateAsync(
                            userId,
                            orgKey,
                            repo.AzureDevOpsProject,
                            repo.RepositoryIdOrName,
                            gitRef.ObjectId,
                            ct);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }

                int? daysSince = lastCommit is { } d
                    ? Math.Max(0, (int)Math.Floor((now - d).TotalDays))
                    : null;

                var isStale = !isProtected &&
                              daysSince is { } days &&
                              days >= thresholdDays;

                if (staleOnly && !isStale)
                    return null;

                return new GitBranchDto(
                    repo.Id,
                    repo.AzureDevOpsOrganization,
                    repo.AzureDevOpsProject,
                    repo.RepositoryIdOrName,
                    repo.ServiceName,
                    branchName,
                    gitRef.Name,
                    gitRef.ObjectId,
                    lastCommit,
                    daysSince,
                    isProtected,
                    isStale);
            });

            var branches = await Task.WhenAll(branchTasks);
            results.AddRange(branches.Where(b => b is not null)!);
        }

        return results
            .OrderByDescending(b => b.IsStale)
            .ThenByDescending(b => b.DaysSinceLastCommit ?? -1)
            .ThenBy(b => b.AzureDevOpsOrganization)
            .ThenBy(b => b.AzureDevOpsProject)
            .ThenBy(b => b.RepositoryIdOrName)
            .ThenBy(b => b.BranchName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<DeleteGitBranchResult> DeleteBranchAsync(
        DeleteGitBranchRequest request,
        CancellationToken ct)
    {
        var userId = _currentUser.GetRequiredUserId();
        if (string.IsNullOrWhiteSpace(request.BranchName))
            throw new ArgumentException("BranchName is required.", nameof(request));

        var repo = await _db.RegisteredRepositories.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RegisteredRepositoryId, ct)
            ?? throw new ArgumentException("Registered repository was not found.", nameof(request.RegisteredRepositoryId));

        var branchName = AzureDevOpsClient.BranchShortName(request.BranchName.Trim());
        if (IsProtectedBranch(branchName))
            throw new InvalidOperationException($"Branch “{branchName}” is protected and cannot be deleted.");

        var targetKey = BuildTargetKey(repo, branchName);
        var orgKey = AzureDevOpsOrganizationService.NormalizeKey(repo.AzureDevOpsOrganization);

        var refs = await _ado.ListBranchRefsAsync(
            userId,
            orgKey,
            repo.AzureDevOpsProject,
            repo.RepositoryIdOrName,
            ct);

        var gitRef = refs.FirstOrDefault(r =>
            string.Equals(AzureDevOpsClient.BranchShortName(r.Name), branchName, StringComparison.OrdinalIgnoreCase));

        if (gitRef is null)
        {
            await WriteActionLogAsync(
                userId,
                targetKey,
                repo.Id,
                branchName,
                success: false,
                errorMessage: "Branch was not found in Azure DevOps.",
                ct);
            return new DeleteGitBranchResult(false, branchName, targetKey, "Branch was not found in Azure DevOps.");
        }

        try
        {
            await _ado.DeleteBranchRefAsync(
                userId,
                orgKey,
                repo.AzureDevOpsProject,
                repo.RepositoryIdOrName,
                gitRef.Name,
                gitRef.ObjectId,
                ct);

            await WriteActionLogAsync(
                userId,
                targetKey,
                repo.Id,
                branchName,
                success: true,
                errorMessage: null,
                ct);

            return new DeleteGitBranchResult(true, branchName, targetKey, null);
        }
        catch (HttpRequestException ex)
        {
            await WriteActionLogAsync(
                userId,
                targetKey,
                repo.Id,
                branchName,
                success: false,
                errorMessage: ex.Message,
                ct);
            return new DeleteGitBranchResult(false, branchName, targetKey, ex.Message);
        }
    }

    public async Task<IReadOnlyList<AdminActionLogDto>> ListActionLogsAsync(
        string? action,
        int limit,
        CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = _db.AdminActionLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(x => x.Action == action.Trim());

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .Select(x => new AdminActionLogDto(
                x.Id,
                x.UserId,
                x.User.DisplayName ?? x.User.Email,
                x.Action,
                x.TargetType,
                x.TargetKey,
                x.DetailsJson,
                x.Success,
                x.ErrorMessage,
                x.CreatedAt))
            .ToListAsync(ct);

        return rows;
    }

    private async Task<List<RegisteredRepository>> LoadRegisteredRepositoriesAsync(
        Guid userId,
        Guid? organizationId,
        Guid? registeredRepositoryId,
        CancellationToken ct)
    {
        var query = _db.RegisteredRepositories.AsNoTracking().AsQueryable();

        if (registeredRepositoryId is { } repoId)
            query = query.Where(r => r.Id == repoId);

        if (organizationId is { } orgId)
        {
            var org = await _db.UserAzureDevOpsOrganizations.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orgId && o.UserId == userId, ct)
                ?? throw new ArgumentException("Organization was not found.", nameof(organizationId));

            var display = org.OrganizationDisplay;
            var key = org.OrganizationKey;
            // NormalizeKey is not translatable by EF Core; ToLower() is, and org slugs
            // are validated ASCII and trimmed on insert, so it is equivalent here.
            query = query.Where(r =>
                r.AzureDevOpsOrganization == display ||
                r.AzureDevOpsOrganization.ToLower() == key);
        }

        return await query
            .OrderBy(r => r.AzureDevOpsOrganization)
            .ThenBy(r => r.AzureDevOpsProject)
            .ThenBy(r => r.RepositoryIdOrName)
            .ToListAsync(ct);
    }

    private async Task WriteActionLogAsync(
        Guid userId,
        string targetKey,
        Guid registeredRepositoryId,
        string branchName,
        bool success,
        string? errorMessage,
        CancellationToken ct)
    {
        var details = JsonSerializer.Serialize(new
        {
            registeredRepositoryId,
            branchName
        });

        _db.AdminActionLogs.Add(new AdminActionLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = "branch.delete",
            TargetType = "git.branch",
            TargetKey = targetKey,
            DetailsJson = details,
            Success = success,
            ErrorMessage = errorMessage is { Length: > 2000 } ? errorMessage[..2000] : errorMessage,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    private static bool IsProtectedBranch(string branchName) =>
        ProtectedBranchNames.Contains(branchName);

    private static string BuildTargetKey(RegisteredRepository repo, string branchName) =>
        $"{repo.AzureDevOpsOrganization}/{repo.AzureDevOpsProject}/{repo.RepositoryIdOrName}:{branchName}";
}
