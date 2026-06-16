using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

public sealed class RequireModuleFilterTests
{
    private readonly ITenantRepository _repo = Substitute.For<ITenantRepository>();

    [Fact]
    public async Task ModuleNotEnabled_Returns403()
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.UpdateModules(["inventory"]); // auto_service NOT enabled
        _repo.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var filter = new RequireModuleFilter("auto_service", _repo);
        var context = BuildContext(tenant.Id, "enterprise_admin");

        await filter.OnActionExecutionAsync(context, NextDelegate());

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task ModuleEnabled_CallsNext()
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.UpdateModules(["inventory", "auto_service"]);
        _repo.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var filter = new RequireModuleFilter("auto_service", _repo);
        var context = BuildContext(tenant.Id, "enterprise_admin");
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
    public async Task TenantNotFound_Returns403()
    {
        var missingTenantId = Guid.NewGuid();
        _repo.GetByIdAsync(missingTenantId, Arg.Any<CancellationToken>()).Returns((Tenant?)null);

        var filter = new RequireModuleFilter("inventory", _repo);
        var context = BuildContext(missingTenantId, "enterprise_admin");

        await filter.OnActionExecutionAsync(context, NextDelegate());

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task MissingTenantClaim_Returns403()
    {
        var filter = new RequireModuleFilter("inventory", _repo);
        var context = BuildContext(tenantId: null, role: "enterprise_admin");

        await filter.OnActionExecutionAsync(context, NextDelegate());

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        await _repo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProviderRole_BypassesCheck_NoRepoLookup()
    {
        var filter = new RequireModuleFilter("auto_service", _repo);
        var context = BuildContext(tenantId: null, role: "provider");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return NextDelegate()();
        });

        Assert.True(nextCalled);
        Assert.Null(context.Result);
        await _repo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static ActionExecutingContext BuildContext(Guid? tenantId, string role)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (tenantId is not null)
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

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
