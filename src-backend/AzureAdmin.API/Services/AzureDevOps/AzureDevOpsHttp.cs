using System.Net;
using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace AzureAdmin.API.Services.AzureDevOps;

/// <summary>
/// Shared send and error handling for dev.azure.com calls, so every failure reaches callers as an
/// <see cref="HttpRequestException"/> (which the services already catch per repository / per pull request)
/// with a short, user-presentable message. The raw response body is logged, never put in the message.
/// </summary>
internal static class AzureDevOpsHttp
{
    /// <summary>Key in <see cref="Exception.Data"/> holding Azure DevOps' error <c>typeKey</c>, when it sent one.</summary>
    public const string TypeKeyDataKey = "AzureDevOps.TypeKey";

    private const int MaxMessageLength = 500;
    private const int MaxLoggedBodyLength = 4000;

    /// <summary>
    /// Sends the request, translating resilience-pipeline rejections and client timeouts into
    /// <see cref="HttpRequestException"/>. Polly's <see cref="TimeoutRejectedException"/> and
    /// <see cref="BrokenCircuitException"/> derive from neither <see cref="HttpRequestException"/> nor
    /// <see cref="TaskCanceledException"/>, so without this they skip every catch block and surface as 500s.
    /// </summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (TimeoutRejectedException ex)
        {
            throw TimedOut(ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout rather than the caller cancelling.
            throw TimedOut(ex);
        }
        catch (BrokenCircuitException ex)
        {
            throw new HttpRequestException(
                "Azure DevOps calls are paused for a moment after repeated failures. Try again in a minute.",
                ex,
                HttpStatusCode.ServiceUnavailable);
        }
    }

    /// <summary>
    /// Builds the exception for a non-success response: <c>Azure DevOps {operation} failed ({status}): {message}</c>,
    /// where the message is Azure DevOps' own <c>message</c> field (for example
    /// <c>TF401179: An active pull request for the source and target branch already exists.</c>).
    /// The full body goes to the log only; it can be an HTML sign-in page or a large JSON payload.
    /// </summary>
    public static HttpRequestException Failure(string operation, HttpResponseMessage response, string body, ILogger logger)
    {
        var status = (int)response.StatusCode;
        logger.LogWarning(
            "Azure DevOps {Operation} failed with {Status} for {Method} {Path}: {Body}",
            operation,
            status,
            response.RequestMessage?.Method,
            response.RequestMessage?.RequestUri?.AbsolutePath,
            Truncate(body, MaxLoggedBodyLength));

        var (message, typeKey) = ParseError(body);
        var text = message is not null
            ? $"Azure DevOps {operation} failed ({status}): {Truncate(message, MaxMessageLength)}"
            : $"Azure DevOps {operation} failed ({status} {response.ReasonPhrase}).";

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NonAuthoritativeInformation)
            text += " Check that the PAT is valid, not expired, and has the required scopes.";

        var exception = new HttpRequestException(text, inner: null, response.StatusCode);
        if (typeKey is not null)
            exception.Data[TypeKeyDataKey] = typeKey;
        return exception;
    }

    /// <summary>Extracts <c>message</c> and <c>typeKey</c> from an Azure DevOps JSON error body.</summary>
    internal static (string? Message, string? TypeKey) ParseError(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.TrimStart() is not ['{', ..])
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()?.Trim()
                : null;
            var typeKey = root.TryGetProperty("typeKey", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
            return (string.IsNullOrEmpty(message) ? null : message, typeKey);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static HttpRequestException TimedOut(Exception inner) =>
        new("Azure DevOps did not respond in time. Try again.", inner, HttpStatusCode.GatewayTimeout);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
