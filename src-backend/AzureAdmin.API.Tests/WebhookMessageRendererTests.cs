using AzureAdmin.API.Models;
using AzureAdmin.API.Services.Webhooks;

namespace AzureAdmin.API.Tests;

public sealed class WebhookMessageRendererTests
{
    [Fact]
    public void BuiltInTemplateMatchesFlatListSample()
    {
        var text = WebhookMessageRenderer.RenderSample();

        Assert.Contains("Created 5 PRs for dev → master targeting 2 teams:", text);
        Assert.Contains("- pr1 - backend", text);
        Assert.Contains("- pr2 - backend", text);
        Assert.Contains("- pr3 - frontend", text);
        Assert.Contains("- pr4 - frontend", text);
        Assert.Contains("- pr5 - frontend", text);
    }

    [Fact]
    public void DefaultCreatedUsesPlaceholders()
    {
        Assert.Contains("{{pr_count}}", WebhookMessageRenderer.DefaultCreated);
        Assert.Contains("{{phase}}", WebhookMessageRenderer.DefaultCreated);
        Assert.Contains("{{team_count}}", WebhookMessageRenderer.DefaultCreated);
        Assert.Contains("{{pr_list}}", WebhookMessageRenderer.DefaultCreated);
    }

    [Fact]
    public void LinkedPrListUsesNameThenTeam()
    {
        var context = new WebhookMessageContext(
            WebhookEventKind.DevToMasterCreated,
            "Release sprint 2026/41",
            "2026/41",
            "dev → master",
            "http://localhost:8080/releases/abc",
            "Ada",
            [
                new WebhookPrLine("backend", "api", 1841, "https://dev.azure.com/o/_git/api/pullrequest/1841"),
                new WebhookPrLine("frontend", "web-app", 902, "https://dev.azure.com/o/_git/web/pullrequest/902")
            ],
            []);

        var text = WebhookMessageRenderer.Render(
            WebhookMessageRenderer.DefaultCreated,
            context,
            markdownLinks: true);

        Assert.Contains("Created 2 PRs for dev → master targeting 2 teams:", text);
        Assert.Contains("- [api](https://dev.azure.com/o/_git/api/pullrequest/1841) - backend", text);
        Assert.Contains("- [web-app](https://dev.azure.com/o/_git/web/pullrequest/902) - frontend", text);
    }

    [Fact]
    public void CustomTemplateUsesPlaceholders()
    {
        var context = new WebhookMessageContext(
            WebhookEventKind.MasterToProdCreated,
            "R1",
            "S1",
            "master → prod",
            null,
            null,
            [new WebhookPrLine("platform", "svc", 7, "https://example/pr/7")],
            []);

        var text = WebhookMessageRenderer.Render(
            "{{release_title}} {{sprint}} {{pr_count}} {{pr_list_plain}}",
            context,
            markdownLinks: true);

        Assert.Contains("R1 S1 1", text);
        Assert.Contains("- svc - platform", text);
    }

    [Fact]
    public void FailedSectionOmittedWhenNothingFailed()
    {
        var context = new WebhookMessageContext(
            WebhookEventKind.DevToMasterMerged,
            "R1",
            null,
            "dev → master",
            null,
            null,
            [new WebhookPrLine("t", "svc", 1, "https://example/1")],
            []);

        var text = WebhookMessageRenderer.Render(
            WebhookMessageRenderer.DefaultMerged,
            context,
            markdownLinks: false);

        Assert.DoesNotContain("Failed", text);
    }

    [Fact]
    public void FailedSectionIncludedWhenCompleteBatchHasErrors()
    {
        var context = new WebhookMessageContext(
            WebhookEventKind.DevToMasterMerged,
            "R1",
            null,
            "dev → master",
            null,
            null,
            [new WebhookPrLine("t", "ok-svc", 1, "https://example/1")],
            [new WebhookPrLine("t", "bad-svc", 2, "https://example/2", "conflicts")]);

        var text = WebhookMessageRenderer.Render(
            WebhookMessageRenderer.DefaultMerged,
            context,
            markdownLinks: false);

        Assert.Contains("Failed (1):", text);
        Assert.Contains("- bad-svc - t — conflicts", text);
    }

    [Fact]
    public void ResolveTemplateFallsBackToDefault()
    {
        Assert.Equal(
            WebhookMessageRenderer.DefaultMerged,
            WebhookMessageRenderer.ResolveTemplate("  ", WebhookEventKind.MasterToProdMerged));
    }

    [Theory]
    [InlineData(ReleasePrPhase.DevToMaster, false, WebhookEventKind.DevToMasterCreated)]
    [InlineData(ReleasePrPhase.DevToMaster, true, WebhookEventKind.DevToMasterMerged)]
    [InlineData(ReleasePrPhase.MasterToProd, false, WebhookEventKind.MasterToProdCreated)]
    [InlineData(ReleasePrPhase.MasterToProd, true, WebhookEventKind.MasterToProdMerged)]
    [InlineData(ReleasePrPhase.ProdToDev, false, null)]
    public void MapsPhaseToToggleEvent(ReleasePrPhase phase, bool merged, WebhookEventKind? expected)
    {
        Assert.Equal(expected, WebhookEvents.From(phase, merged));
    }
}
