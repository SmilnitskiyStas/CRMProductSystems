using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Infrastructure;

/// <summary>
/// JwtBearer OnTokenValidated hook for cashier-app operator tokens: (1) the token's audience is
/// separate ("shelfguard:register") and only endpoints marked <see cref="AllowOperatorTokenAttribute"/>
/// accept it; (2) while valid it must still map to an open session of an active register, an active
/// POS-role user and a tenant with the 'pos' module (cached ~30 s, invalidated on end/revoke).
/// </summary>
public static class OperatorTokenValidation
{
    public static async Task ValidateAsync(TokenValidatedContext ctx, string registerAudience)
    {
        var principal = ctx.Principal;
        if (principal is null) return;

        var isOperatorToken = principal.FindAll("aud").Any(c => c.Value == registerAudience)
                              || principal.HasClaim(c => c.Type == "session_id");
        if (!isOperatorToken) return;

        var endpoint = ctx.HttpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<AllowOperatorTokenAttribute>() is null)
        {
            ctx.Fail("Operator token is not accepted by this endpoint.");
            return;
        }

        if (!Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId)
            || !Guid.TryParse(principal.FindFirstValue("session_id"), out var sessionId)
            || !Guid.TryParse(principal.FindFirstValue("register_id"), out var registerId)
            || !Guid.TryParse(principal.FindFirstValue("cashier_id"), out var userId))
        {
            ctx.Fail("Malformed operator token.");
            return;
        }

        var validator = ctx.HttpContext.RequestServices.GetRequiredService<IOperatorSessionValidator>();
        if (!await validator.IsValidAsync(tenantId, sessionId, registerId, userId, ctx.HttpContext.RequestAborted))
            ctx.Fail("Operator session is no longer valid.");
    }
}
