using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Gates an endpoint behind a tenant module being enabled (ADR-015).
/// Usage: [RequireModule("auto_service")] on a controller or action.
/// Returns 403 { error: "Module not activated" } when the calling tenant
/// doesn't have the module in its enabled-modules list. Provider-role callers
/// (no tenant context outside impersonation) bypass the check.
/// </summary>
public sealed class RequireModuleAttribute : Attribute, IFilterFactory
{
    private readonly string _moduleKey;

    public RequireModuleAttribute(string moduleKey) => _moduleKey = moduleKey;

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var repo = serviceProvider.GetRequiredService<ITenantRepository>();
        return new RequireModuleFilter(_moduleKey, repo);
    }
}

internal sealed class RequireModuleFilter : IAsyncActionFilter
{
    private static readonly ObjectResult ModuleNotActivated =
        new(new { error = "Module not activated" }) { StatusCode = StatusCodes.Status403Forbidden };

    private readonly string _moduleKey;
    private readonly ITenantRepository _repo;

    public RequireModuleFilter(string moduleKey, ITenantRepository repo)
    {
        _moduleKey = moduleKey;
        _repo = repo;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var role = user.FindFirstValue(ClaimTypes.Role);

        // Provider manages all tenants from the admin panel, not through module-gated
        // tenant endpoints. Impersonation issues a JWT with the impersonated user's own
        // role/tenant_id, so it goes through the normal check below, not this branch.
        if (string.Equals(role, "provider", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var tenantIdRaw = user.FindFirstValue("tenant_id");
        if (!Guid.TryParse(tenantIdRaw, out var tenantId) || tenantId == Guid.Empty)
        {
            context.Result = ModuleNotActivated;
            return;
        }

        var tenant = await _repo.GetByIdAsync(tenantId, context.HttpContext.RequestAborted);
        if (tenant is null || !tenant.HasModule(_moduleKey))
        {
            context.Result = ModuleNotActivated;
            return;
        }

        await next();
    }
}
