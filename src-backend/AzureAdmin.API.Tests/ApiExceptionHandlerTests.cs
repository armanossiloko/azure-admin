using System.Net;
using AzureAdmin.API.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace AzureAdmin.API.Tests;

public class ApiExceptionHandlerTests
{
    /// <summary>Throws and catches so the exception carries a real throw site (this test namespace counts as application code).</summary>
    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task ThrowFromAsyncAsync(string message)
    {
        await Task.Yield();
        throw new InvalidOperationException(message);
    }

    [Fact]
    public void ArgumentException_from_application_code_maps_to_400_with_its_message()
    {
        var (status, _, message) = ApiExceptionHandler.Map(Thrown(new ArgumentException("Organization is required.")));

        Assert.Equal(400, status);
        Assert.Equal("Organization is required.", message);
    }

    [Fact]
    public void InvalidOperationException_from_application_code_maps_to_400_with_its_message()
    {
        var (status, _, message) = ApiExceptionHandler.Map(Thrown(new InvalidOperationException("Release was not found.")));

        Assert.Equal(400, status);
        Assert.Equal("Release was not found.", message);
    }

    [Fact]
    public async Task InvalidOperationException_from_application_async_method_maps_to_400()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ThrowFromAsyncAsync("No valid PAT."));

        var (status, _, message) = ApiExceptionHandler.Map(ex);

        Assert.Equal(400, status);
        Assert.Equal("No valid PAT.", message);
    }

    [Fact]
    public void InvalidOperationException_from_framework_code_is_a_500_without_its_message()
    {
        // Thrown inside System.Linq, not by this application: a bug, not a client error.
        var ex = Assert.Throws<InvalidOperationException>(() => new List<int>().Single());

        var (status, _, message) = ApiExceptionHandler.Map(ex);

        Assert.Equal(500, status);
        Assert.DoesNotContain("Sequence", message);
    }

    [Fact]
    public void Unthrown_exception_has_no_throw_site_and_is_treated_as_internal()
    {
        var (status, _, _) = ApiExceptionHandler.Map(new InvalidOperationException("never thrown"));

        Assert.Equal(500, status);
    }

    [Fact]
    public void HttpRequestException_maps_to_502()
    {
        var (status, _, message) = ApiExceptionHandler.Map(new HttpRequestException("Azure DevOps get PR failed (401): denied"));

        Assert.Equal(502, status);
        Assert.Contains("401", message);
    }

    [Fact]
    public void TaskCanceledException_maps_to_504()
    {
        var (status, _, _) = ApiExceptionHandler.Map(new TaskCanceledException());

        Assert.Equal(504, status);
    }

    [Fact]
    public void Polly_timeout_maps_to_504()
    {
        var (status, _, _) = ApiExceptionHandler.Map(new TimeoutRejectedException());

        Assert.Equal(504, status);
    }

    [Fact]
    public void Open_circuit_maps_to_503()
    {
        var (status, _, _) = ApiExceptionHandler.Map(new BrokenCircuitException());

        Assert.Equal(503, status);
    }

    [Fact]
    public void Translated_azure_devops_timeout_maps_to_504_with_its_message()
    {
        var ex = new HttpRequestException("Azure DevOps did not respond in time. Try again.", new TimeoutRejectedException(), HttpStatusCode.GatewayTimeout);

        var (status, _, message) = ApiExceptionHandler.Map(ex);

        Assert.Equal(504, status);
        Assert.Equal(ex.Message, message);
    }

    [Fact]
    public void Translated_open_circuit_maps_to_503()
    {
        var ex = new HttpRequestException("paused", new BrokenCircuitException(), HttpStatusCode.ServiceUnavailable);

        var (status, _, _) = ApiExceptionHandler.Map(ex);

        Assert.Equal(503, status);
    }

    [Fact]
    public void Azure_devops_503_response_without_open_circuit_stays_a_502()
    {
        // Azure DevOps itself answering 503 is an upstream failure, not our breaker pausing calls.
        var ex = new HttpRequestException("Azure DevOps get PR failed (503).", null, HttpStatusCode.ServiceUnavailable);

        var (status, _, _) = ApiExceptionHandler.Map(ex);

        Assert.Equal(502, status);
    }

    [Fact]
    public void Concurrency_conflict_maps_to_409()
    {
        var (status, _, _) = ApiExceptionHandler.Map(new DbUpdateConcurrencyException("row changed"));

        Assert.Equal(409, status);
    }

    [Fact]
    public void Unknown_exception_maps_to_500_without_leaking_the_message()
    {
        var (status, _, message) = ApiExceptionHandler.Map(Thrown(new NullReferenceException("secret internal detail")));

        Assert.Equal(500, status);
        Assert.DoesNotContain("secret", message);
    }
}
