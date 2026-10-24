using ShelfGuard.Application.Common;

namespace ShelfGuard.Api.Infrastructure;

/// <summary>Block every legacy private upload URL before StaticFiles, on every API role.</summary>
public sealed class PrivateUploadGuard(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var segments = (context.Request.Path.Value ?? "").Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Match filesystem aliases too (duplicate separators, Windows trailing dots/spaces),
        // and reject dot-segment traversal before StaticFiles can canonicalize it.
        var privatePath = segments.Length >= 2
            && segments[0].TrimEnd('.', ' ').Equals("uploads", StringComparison.OrdinalIgnoreCase)
            && PrivateUploadPaths.Categories.Contains(segments[1].TrimEnd('.', ' '), StringComparer.OrdinalIgnoreCase);
        if (privatePath || segments.Any(s => s is "." or ".."))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.Headers.CacheControl = "no-store";
            return;
        }
        await next(context);
    }
}
