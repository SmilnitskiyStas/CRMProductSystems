using Microsoft.AspNetCore.Diagnostics;

namespace ShelfGuard.Api.Infrastructure;

/// <summary>
/// TASK-582: last-resort catch for any unhandled exception that reaches the API boundary (e.g.
/// an uncaught PostgresException from an RLS violation — see SupplierAgreementService.
/// MarkSignedAsync's TASK-582 fix for the concrete bug this generalizes from). Before this
/// existed, Program.cs had no exception-handling middleware at all, so an unhandled exception
/// aborted the connection before headers were sent — the browser reported a misleading CORS
/// error instead of a clean 500.
///
/// Registered via AddExceptionHandler&lt;GlobalExceptionHandler&gt;() + app.UseExceptionHandler()
/// in Program.cs, placed BEFORE UseCors() so the CORS middleware still gets to add
/// Access-Control-Allow-Origin to this error response — otherwise the exception handler would
/// reproduce the exact masking bug it exists to fix, just one layer up.
///
/// Follows the project-wide { "error": "..." } response contract (api-contracts.md) — same
/// shape as <see cref="ErrorBodyClientErrorFactory"/>. Never leaks exception details or stack
/// traces to the client; the full exception goes to the server-side log only.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is ShelfGuard.Application.Services.WorkAccessDeniedException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(new { error = "Access denied." }, cancellationToken);
            return true;
        }
        if (exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            await httpContext.Response.WriteAsJsonAsync(new { error = "The record changed. Reload and try again.", code = "concurrent_update" }, cancellationToken);
            return true;
        }
        // An optional dependency (AI, weather, geocoder) is down / its breaker is open: that is
        // a degraded feature, not a server bug — 503 with a stable code the UI can key off.
        if (exception is ShelfGuard.Application.Services.ExternalServiceUnavailableException unavailable)
        {
            _logger.LogWarning(
                "External service '{Service}' unavailable on {Method} {Path}",
                unavailable.Service, httpContext.Request.Method, httpContext.Request.Path);

            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            httpContext.Response.Headers.RetryAfter = "30";
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsJsonAsync(
                new { error = "Service temporarily unavailable. Try again shortly.", code = "service_unavailable", service = unavailable.Service },
                cancellationToken);
            return true;
        }

        _logger.LogError(
            exception, "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsJsonAsync(
            new { error = "Internal server error." }, cancellationToken);

        return true;
    }
}
