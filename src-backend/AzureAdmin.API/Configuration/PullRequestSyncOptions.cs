using System.ComponentModel.DataAnnotations;

namespace AzureAdmin.API.Configuration;

/// <summary>
/// Background pull request status sync (<c>PullRequestSync</c> section). Enabled by default; every
/// <see cref="IntervalMinutes"/> the API checks the Azure DevOps status of every still-active PR on
/// Draft/Active releases so merged PRs, phase-complete chat messages, and dashboard counts no longer
/// depend on somebody opening the release page.
/// </summary>
public sealed class PullRequestSyncOptions
{
    public const string SectionName = "PullRequestSync";

    public bool Enabled { get; set; } = true;

    [Range(1, 24 * 60)]
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Delay after startup before the first run, so migrations and warm-up finish first.</summary>
    [Range(0, 3600)]
    public int InitialDelaySeconds { get; set; } = 30;

    /// <summary>Upper bound on concurrent Azure DevOps lookups per release during a sync.</summary>
    [Range(1, 16)]
    public int MaxConcurrency { get; set; } = 4;
}
