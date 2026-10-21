using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AzureAdmin.API.Infrastructure;

/// <summary>
/// Turns exceptions that escape a controller into a JSON error response instead of an empty 500.
/// The body is RFC 7807 problem details plus a top-level <c>message</c>, which is the shape every
/// hand-written controller error already uses and the SPA reads (<c>error.message</c>).
/// Argument/InvalidOperation exceptions thrown by this application's own code are domain errors and map
/// to 400 with their message, like the controllers do. The same exception types thrown by the framework
/// (OIDC metadata, EF query translation, ...) are bugs or misconfiguration: 500 with a generic message,
/// details only in the log.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private const string ApplicationNamespacePrefix = "AzureAdmin.";

    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Client went away; nothing to answer and nothing worth logging as an error.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var (status, title, message) = Map(exception);

        if (status >= 500)
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        else
            _logger.LogInformation("{Method} {Path} -> {Status}: {Message}", httpContext.Request.Method, httpContext.Request.Path, status, message);

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = message,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["message"] = message;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        });
    }

    /// <summary>Maps an exception to (status, title, client-visible message). Pure; unit-tested.</summary>
    public static (int Status, string Title, string Message) Map(Exception exception) => exception switch
    {
        // Services throw these for "not found" / "bad input" / "not allowed in this state".
        ArgumentException ex when IsThrownByApplication(ex) => (StatusCodes.Status400BadRequest, "Bad request", ex.Message),
        InvalidOperationException ex when IsThrownByApplication(ex) => (StatusCodes.Status400BadRequest, "Bad request", ex.Message),

        // Azure DevOps (or another upstream) rejected or failed the call.
        HttpRequestException ex => (StatusCodes.Status502BadGateway, "Upstream request failed", ex.Message),
        TaskCanceledException => (StatusCodes.Status504GatewayTimeout, "Upstream request timed out", "The upstream request timed out. Try again."),

        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict", "The record was changed by someone else. Reload and try again."),

        _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred. Check the backend logs for details.")
    };

    /// <summary>
    /// True when the throw site is in this application (including compiler-generated async state machines,
    /// which are nested in the declaring type and keep its namespace).
    /// </summary>
    public static bool IsThrownByApplication(Exception exception)
    {
        var ns = exception.TargetSite?.DeclaringType?.Namespace;
        return ns is not null && ns.StartsWith(ApplicationNamespacePrefix, StringComparison.Ordinal);
    }
}
