using System.Net;
using AzureAdmin.API.Services.AzureDevOps;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace AzureAdmin.API.Tests;

public sealed class AzureDevOpsHttpTests
{
    [Fact]
    public async Task Pipeline_timeout_becomes_an_HttpRequestException_with_504()
    {
        using var http = new HttpClient(new ThrowingHandler(new TimeoutRejectedException()));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(http));

        Assert.Equal(HttpStatusCode.GatewayTimeout, ex.StatusCode);
        Assert.IsType<TimeoutRejectedException>(ex.InnerException);
    }

    [Fact]
    public async Task Open_circuit_becomes_an_HttpRequestException_with_503()
    {
        using var http = new HttpClient(new ThrowingHandler(new BrokenCircuitException()));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(http));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.IsType<BrokenCircuitException>(ex.InnerException);
    }

    [Fact]
    public async Task Client_timeout_becomes_an_HttpRequestException_with_504()
    {
        using var http = new HttpClient(new ThrowingHandler(new TaskCanceledException("HttpClient.Timeout")));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(http));

        Assert.Equal(HttpStatusCode.GatewayTimeout, ex.StatusCode);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_translated()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var http = new HttpClient(new ThrowingHandler(new TaskCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(http, cts.Token));
    }

    [Fact]
    public void Failure_uses_the_azure_devops_message_and_never_the_raw_body()
    {
        const string body = """{"$id":"1","message":"TF401179: An active pull request for the source and target branch already exists.","typeKey":"GitPullRequestExistsException","errorCode":0}""";

        var ex = AzureDevOpsHttp.Failure("PR create", Response(HttpStatusCode.Conflict), body, NullLogger.Instance);

        Assert.Equal("Azure DevOps PR create failed (409): TF401179: An active pull request for the source and target branch already exists.", ex.Message);
        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.Equal("GitPullRequestExistsException", ex.Data[AzureDevOpsHttp.TypeKeyDataKey]);
        Assert.DoesNotContain("$id", ex.Message);
    }

    [Fact]
    public void Failure_with_a_non_json_body_reports_only_the_status_and_a_pat_hint_for_auth_errors()
    {
        const string html = "<html><body>Sign in to your account ... secret-looking markup ...</body></html>";

        var ex = AzureDevOpsHttp.Failure("get PR", Response(HttpStatusCode.Unauthorized), html, NullLogger.Instance);

        Assert.StartsWith("Azure DevOps get PR failed (401 Unauthorized).", ex.Message);
        Assert.Contains("PAT", ex.Message);
        Assert.DoesNotContain("<html>", ex.Message);
    }

    [Fact]
    public void Unresolvable_branch_keeps_its_type_key_for_callers_that_detect_it()
    {
        const string body = """{"message":"TF401175: The version descriptor <Branch: prod> could not be resolved to a version commit.","typeKey":"GitUnresolvableToCommitException"}""";

        var ex = AzureDevOpsHttp.Failure("commits", Response(HttpStatusCode.NotFound), body, NullLogger.Instance);

        Assert.Equal("GitUnresolvableToCommitException", ex.Data[AzureDevOpsHttp.TypeKeyDataKey]);
        Assert.Contains("could not be resolved", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ broken")]
    [InlineData("""{"value":[]}""")]
    public void ParseError_returns_nothing_for_bodies_without_a_message(string body)
    {
        var (message, typeKey) = AzureDevOpsHttp.ParseError(body);

        Assert.Null(message);
        Assert.Null(typeKey);
    }

    private static async Task SendAsync(HttpClient http, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://dev.azure.com/org/project/_apis/git/repositories/repo/pullrequests/1");
        using var _ = await AzureDevOpsHttp.SendAsync(http, request, ct);
    }

    private static HttpResponseMessage Response(HttpStatusCode status) =>
        new(status) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://dev.azure.com/org/_apis/x") };

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<HttpResponseMessage>(exception);
        }
    }
}
