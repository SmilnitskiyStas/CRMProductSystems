using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

/// <summary>
/// TASK-543 — RequireConsumerFeatureFilter: tenantId resolution from a {tenantId} route segment
/// or a ?tenantId= query parameter (never a JWT claim — see the attribute's remarks and
/// RequireModuleFilterTests for that unrelated staff-side sibling), route taking precedence, and
/// the enabled/disabled/missing-tenant branches. IConsumerFeatureFlagService's own
/// default-enabled production-safety behavior is covered by ConsumerFeatureFlagServiceTests, not
/// re-tested here.
/// </summary>
public sealed class RequireConsumerFeatureFilterTests
{
    private readonly IConsumerFeatureFlagService _flags = Substitute.For<IConsumerFeatureFlagService>();

    [Fact]
    public async Task FeatureDisabled_Returns403()
    {
        var tenantId = Guid.NewGuid();
        _flags.IsEnabledAsync(tenantId, "loyalty", Arg.Any<CancellationToken>()).Returns(false);

        var filter = new RequireConsumerFeatureFilter("loyalty", _flags);
        var context = BuildContext(routeTenantId: tenantId);

        await filter.OnActionExecutionAsync(context, NextDelegate());

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task FeatureEnabled_CallsNext()
    {
        var tenantId = Guid.NewGuid();
        _flags.IsEnabledAsync(tenantId, "loyalty", Arg.Any<CancellationToken>()).Returns(true);

        var filter = new RequireConsumerFeatureFilter("loyalty", _flags);
        var context = BuildContext(routeTenantId: tenantId);
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return NextDelegate()();
        });

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task ResolvesTenantId_FromQueryString_WhenNoRouteValue()
    {
        var tenantId = Guid.NewGuid();
        _flags.IsEnabledAsync(tenantId, "catalog", Arg.Any<CancellationToken>()).Returns(true);

        var filter = new RequireConsumerFeatureFilter("catalog", _flags);
        var context = BuildContext(routeTenantId: null, queryTenantId: tenantId);

        await filter.OnActionExecutionAsync(context, NextDelegate());

        Assert.Null(context.Result);
        await _flags.Received(1).IsEnabledAsync(tenantId, "catalog", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteTenantId_TakesPrecedenceOverQuery()
    {
        var routeTenantId = Guid.NewGuid();
        var queryTenantId = Guid.NewGuid();
        _flags.IsEnabledAsync(routeTenantId, "news", Arg.Any<CancellationToken>()).Returns(true);

        var filter = new RequireConsumerFeatureFilter("news", _flags);
        var context = BuildContext(routeTenantId: routeTenantId, queryTenantId: queryTenantId);

        await filter.OnActionExecutionAsync(context, NextDelegate());

        await _flags.Received(1).IsEnabledAsync(routeTenantId, "news", Arg.Any<CancellationToken>());
        await _flags.DidNotReceive().IsEnabledAsync(queryTenantId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingTenantId_Returns400_NoFlagLookup()
    {
        var filter = new RequireConsumerFeatureFilter("loyalty", _flags);
        var context = BuildContext(routeTenantId: null, queryTenantId: null);

        await filter.OnActionExecutionAsync(context, NextDelegate());

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        await _flags.DidNotReceive().IsEnabledAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static ActionExecutingContext BuildContext(Guid? routeTenantId, Guid? queryTenantId = null)
    {
        var httpContext = new DefaultHttpContext();
        if (queryTenantId is not null)
        {
            httpContext.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
            {
                ["tenantId"] = queryTenantId.Value.ToString(),
            });
        }

        var routeData = new RouteData();
        if (routeTenantId is not null)
        {
            routeData.Values["tenantId"] = routeTenantId.Value.ToString();
        }

        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());
    }

    private static ActionExecutionDelegate NextDelegate() => () =>
        Task.FromResult(new ActionExecutedContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            controller: new object()));
}
