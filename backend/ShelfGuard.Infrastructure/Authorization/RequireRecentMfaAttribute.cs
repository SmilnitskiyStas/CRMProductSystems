using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace ShelfGuard.Infrastructure.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireRecentMfaAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) { context.Result = new UnauthorizedResult(); return; }
        var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds();
        if (!long.TryParse(user.FindFirst("mfa_at")?.Value, out var verified) || verified > now || now - verified > 300)
            context.Result = user.HasClaim("impersonated", "true")
                ? new ObjectResult(new { error = "Exit impersonation and confirm MFA before starting it again.", code = "mfa_reauthentication_required" }) { StatusCode = 403 }
                : new ObjectResult(new { error = "Confirm MFA before this operation.", code = "mfa_step_up_required" }) { StatusCode = 403 };
    }
}
