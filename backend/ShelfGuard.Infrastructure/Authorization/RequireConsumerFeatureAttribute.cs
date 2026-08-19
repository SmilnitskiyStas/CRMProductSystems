using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application.Features.MobileConfig;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Gates a consumer-facing endpoint behind one of the 8 consumer-app feature flags (TASK-543,
/// <see cref="IConsumerFeatureFlagService"/>). Deliberately a DIFFERENT type from
/// <see cref="RequireModuleAttribute"/>, not a variant of it — that attribute resolves the
/// tenant from a <c>tenant_id</c> JWT claim, which a consumer/anonymous session structurally
/// never carries (it is cross-tenant by design; see that attribute's remarks), and it gates a
/// different concept entirely (B2B module licensing, <c>Tenant.Modules</c>). Named distinctly on
/// purpose so nobody confuses the two gate types:
/// <list type="bullet">
/// <item><see cref="RequireModuleAttribute"/> — staff-side, whole B2B module active (inventory/pos/loyalty…).</item>
/// <item><see cref="RequireConsumerFeatureAttribute"/> — consumer-app section visibility per
/// tenant, sourced from <c>MobileConfigurationVersion.ConfigurationJson.features</c>, defaults
/// to enabled until the tenant actually publishes a config saying otherwise — see
/// <see cref="IConsumerFeatureFlagService"/> remarks for the production-safety rationale.</item>
/// </list>
///
/// Usage: <c>[RequireConsumerFeature("loyalty")]</c> on a controller or action whose
/// <c>tenantId</c> travels either as a <c>{tenantId}</c> route segment
/// (<c>ConsumerContentController</c>/<c>ConsumerLoyaltyController</c>'s pattern) or a
/// <c>?tenantId=</c> query parameter (<c>MobileConfigController</c>'s pattern) — this filter
/// checks both, route value first.
///
/// NOT WIRED ONTO ANY LIVE ENDPOINT YET — this is a deliberate TASK-543 scope boundary, not an
/// oversight. Attaching this to <c>ConsumerContentController</c>/<c>ConsumerLoyaltyController</c>
/// (both already live in production, unconditionally, today) is follow-up work for whichever
/// task first makes Publish (TASK-544/546) real; doing it now would 403 every real consumer
/// request for every tenant, since no tenant has published a config yet.
/// </summary>
public sealed class RequireConsumerFeatureAttribute : Attribute, IFilterFactory
{
    private readonly string _flagKey;

    public RequireConsumerFeatureAttribute(string flagKey) => _flagKey = flagKey;

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var flagService = serviceProvider.GetRequiredService<IConsumerFeatureFlagService>();
        return new RequireConsumerFeatureFilter(_flagKey, flagService);
    }
}

internal sealed class RequireConsumerFeatureFilter : IAsyncActionFilter
{
    private static readonly ObjectResult FeatureNotEnabled =
        new(new { error = "Feature not enabled" }) { StatusCode = StatusCodes.Status403Forbidden };

    private static readonly ObjectResult TenantIdRequired =
        new(new { error = "tenantId route or query parameter is required." }) { StatusCode = StatusCodes.Status400BadRequest };

    private readonly string _flagKey;
    private readonly IConsumerFeatureFlagService _flagService;

    public RequireConsumerFeatureFilter(string flagKey, IConsumerFeatureFlagService flagService)
    {
        _flagKey = flagKey;
        _flagService = flagService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var tenantId = ResolveTenantId(context);
        if (tenantId is null || tenantId == Guid.Empty)
        {
            context.Result = TenantIdRequired;
            return;
        }

        var enabled = await _flagService.IsEnabledAsync(tenantId.Value, _flagKey, context.HttpContext.RequestAborted);
        if (!enabled)
        {
            context.Result = FeatureNotEnabled;
            return;
        }

        await next();
    }

    /// <summary>
    /// Route parameter first (<c>{tenantId:guid}</c> — <c>ConsumerContentController</c>/
    /// <c>ConsumerLoyaltyController</c>'s pattern for their tenant-scoped endpoints), then query
    /// string (<c>?tenantId=</c> — <c>MobileConfigController</c>'s pattern). A consumer/anonymous
    /// session structurally never carries a <c>tenant_id</c> JWT claim (see
    /// <see cref="RequireModuleAttribute"/> remarks for why that mechanism can't be reused here),
    /// so this must resolve the same two ways those existing controllers already do.
    /// </summary>
    private static Guid? ResolveTenantId(ActionExecutingContext context)
    {
        if (context.RouteData.Values.TryGetValue("tenantId", out var routeValue) &&
            Guid.TryParse(routeValue?.ToString(), out var routeTenantId))
        {
            return routeTenantId;
        }

        if (context.HttpContext.Request.Query.TryGetValue("tenantId", out var queryValue) &&
            Guid.TryParse(queryValue.ToString(), out var queryTenantId))
        {
            return queryTenantId;
        }

        return null;
    }
}
