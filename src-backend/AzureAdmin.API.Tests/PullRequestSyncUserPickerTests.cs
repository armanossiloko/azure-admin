using AzureAdmin.API.Services.Releases;

namespace AzureAdmin.API.Tests;

public class PullRequestSyncUserPickerTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static PatHolder Holder(Guid user, string org, int daysAgo) =>
        new(user, org, DateTimeOffset.UtcNow.AddDays(-daysAgo));

    [Fact]
    public void Prefers_the_creator_when_they_still_hold_a_pat_for_the_org()
    {
        var holders = new[] { Holder(Alice, "contoso", 10), Holder(Bob, "contoso", 1) };

        var picked = PullRequestSyncUserPicker.Pick(Alice, "Contoso", holders);

        Assert.Equal(Alice, picked);
    }

    [Fact]
    public void Falls_back_to_the_most_recently_saved_pat_when_the_creator_has_none()
    {
        var holders = new[] { Holder(Alice, "contoso", 10), Holder(Bob, "contoso", 1) };

        var picked = PullRequestSyncUserPicker.Pick(Guid.NewGuid(), "contoso", holders);

        Assert.Equal(Bob, picked);
    }

    [Fact]
    public void Legacy_rows_without_a_creator_use_any_holder()
    {
        var holders = new[] { Holder(Alice, "contoso", 3) };

        var picked = PullRequestSyncUserPicker.Pick(null, "contoso", holders);

        Assert.Equal(Alice, picked);
    }

    [Fact]
    public void Returns_null_when_nobody_holds_a_pat_for_the_org()
    {
        var holders = new[] { Holder(Alice, "fabrikam", 3) };

        var picked = PullRequestSyncUserPicker.Pick(Alice, "contoso", holders);

        Assert.Null(picked);
    }

    [Fact]
    public void Organization_match_is_case_and_whitespace_insensitive()
    {
        var holders = new[] { Holder(Alice, "contoso", 3) };

        var picked = PullRequestSyncUserPicker.Pick(null, "  CONTOSO ", holders);

        Assert.Equal(Alice, picked);
    }
}
