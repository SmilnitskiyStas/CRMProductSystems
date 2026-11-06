using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Provider-only variant of <see cref="RequireRecentMfaAttribute"/>: fresh MFA (mfa_at &lt;= 300 s) is required
/// only when the user has 2FA enabled. Enrollment is read live from the database (never from the token), so an
/// old token cannot bypass the requirement after 2FA is connected. Impersonated sessions get no relaxation,
/// and any failure to determine the state fails closed (MFA required).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireRecentMfaIfEnrolledAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) { context.Result = new UnauthorizedResult(); return; }

        if (!user.HasClaim("impersonated", "true") && !await IsMfaEnrolledOrUnknownAsync(context, user)) return;

        new RequireRecentMfaAttribute().OnAuthorization(context);
    }

    private static async Task<bool> IsMfaEnrolledOrUnknownAsync(AuthorizationFilterContext context, ClaimsPrincipal user)
    {
        try
        {
            if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var id)) return true;
            var entity = await context.HttpContext.RequestServices.GetRequiredService<IUserRepository>()
                .GetByIdAsync(id, context.HttpContext.RequestAborted);
            return entity is null || entity.TotpEnabled;
        }
        catch (Exception ex)
        {
            context.HttpContext.RequestServices.GetService<ILoggerFactory>()
                ?.CreateLogger<RequireRecentMfaIfEnrolledAttribute>().LogWarning(ex, "2FA state lookup failed; requiring MFA (fail closed).");
            return true;
        }
    }
}
