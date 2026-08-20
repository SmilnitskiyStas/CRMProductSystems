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
