using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Production;

/// <summary>
/// TASK-754: worker-only internal endpoint for the per-tenant production-demand run —
/// shared-secret filter, controller status mapping, and the TenantContext job binding that must
/// never swap a staff request's tenant. RLS isolation of the run itself is proven against live
/// Postgres in ProductionDemandTenantRunnerRlsIntegrationTests.
/// </summary>
public sealed class InternalProductionDemandTests
{
    private const string Key = "0123456789abcdef0123456789abcdef-test-key";

    // ── InternalJobKeyFilter ────────────────────────────────────────────────

    private static AuthorizationFilterContext Run(string? configuredKey, Action<HttpRequest>? setup = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [InternalJobKeyFilter.ConfigKey] = configuredKey })
            .Build();
        var http = new DefaultHttpContext();
        setup?.Invoke(http.Request);
        var ctx = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
        new InternalJobKeyFilter(config, NullLogger<InternalJobKeyFilter>.Instance).OnAuthorization(ctx);
        return ctx;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short-key")]
    public void Filter_KeyUnsetOrShort_EndpointDisabled404(string? configured)
    {
        var ctx = Run(configured, r => r.Headers[InternalJobKeyFilter.HeaderName] = configured ?? "");
        Assert.IsType<NotFoundResult>(ctx.Result);
    }

    [Fact]
    public void Filter_MissingHeader_401()
    {
        Assert.IsType<UnauthorizedObjectResult>(Run(Key).Result);
    }

    [Fact]
    public void Filter_WrongKey_401()
    {
        Assert.IsType<UnauthorizedObjectResult>(
            Run(Key, r => r.Headers[InternalJobKeyFilter.HeaderName] = Key + "x").Result);
        Assert.IsType<UnauthorizedObjectResult>(
            Run(Key, r => r.Headers[InternalJobKeyFilter.HeaderName] = Key.ToUpperInvariant()).Result);
    }

    [Fact]
    public void Filter_WithAuthorizationHeader_400_EvenWithValidKey()
    {
        var ctx = Run(Key, r =>
        {
            r.Headers[InternalJobKeyFilter.HeaderName] = Key;
            r.Headers.Authorization = "Bearer some.user.jwt";
        });
        Assert.IsType<BadRequestObjectResult>(ctx.Result);
    }

    [Fact]
    public void Filter_ValidKey_Passes()
    {
        Assert.Null(Run(Key, r => r.Headers[InternalJobKeyFilter.HeaderName] = Key).Result);
    }

    // ── InternalJobsController ──────────────────────────────────────────────

    private readonly IProductionDemandTenantRunner _runner = Substitute.For<IProductionDemandTenantRunner>();

    private InternalJobsController Controller() => new(_runner, Substitute.For<ShelfGuard.Application.Features.Receipts.IAutoReplenishmentTenantRunner>())
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    [Fact]
    public async Task Controller_EmptyTenant_400_RunnerNotCalled()
    {
        var result = await Controller().RunProductionDemand(new(Guid.Empty), default);
        Assert.IsType<BadRequestObjectResult>(result);
        await _runner.DidNotReceiveWithAnyArgs().RunAsync(default, default);
    }

    [Fact]
    public async Task Controller_UnknownTenant_404()
    {
        var id = Guid.NewGuid();
        _runner.RunAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ProductionDemandRunResult(ProductionDemandRunStatus.TenantNotFound, [], "Tenant not found."));
        Assert.IsType<NotFoundObjectResult>(await Controller().RunProductionDemand(new(id), default));
    }

    [Fact]
    public async Task Controller_TenantWithoutProductionModule_422Skipped()
    {
        var id = Guid.NewGuid();
        _runner.RunAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ProductionDemandRunResult(ProductionDemandRunStatus.Skipped, [], "Tenant has no production module."));
        var result = Assert.IsType<UnprocessableEntityObjectResult>(await Controller().RunProductionDemand(new(id), default));
        var body = Assert.IsType<InternalJobsController.ProductionDemandRunResponse>(result.Value);
        Assert.Equal("skipped", body.Status);
        Assert.Equal(id, body.TenantId);
    }

    [Fact]
    public async Task Controller_Completed_200_WithDraftSummary_RunsOnlyRequestedTenant()
    {
        var id = Guid.NewGuid();
        var draft = new ProductionRequestDetailDto(
            Guid.NewGuid(), Guid.NewGuid(), "Store 1", Guid.NewGuid(), "Kitchen", DateOnly.FromDateTime(DateTime.UtcNow),
            "draft", "auto_buffer", null, null, DateTime.UtcNow, null, null, null, null, null,
            [new(Guid.NewGuid(), Guid.NewGuid(), "Bread", "шт", 5m, null, null, "r")]);
        _runner.RunAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ProductionDemandRunResult(ProductionDemandRunStatus.Completed, [draft]));

        var ok = Assert.IsType<OkObjectResult>(await Controller().RunProductionDemand(new(id), default));
        var body = Assert.IsType<InternalJobsController.ProductionDemandRunResponse>(ok.Value);
        Assert.Equal("completed", body.Status);
        var d = Assert.Single(body.Drafts);
        Assert.Equal(1, d.LineCount);
        await _runner.Received(1).RunAsync(id, Arg.Any<CancellationToken>());
        await _runner.DidNotReceive().RunAsync(Arg.Is<Guid>(g => g != id), Arg.Any<CancellationToken>());
    }

    // ── TenantContext job binding ───────────────────────────────────────────

    private static TenantContext ContextWith(ClaimsPrincipal? user)
    {
        var http = new DefaultHttpContext();
        if (user is not null) http.User = user;
        return new TenantContext(new HttpContextAccessor { HttpContext = http });
    }

    [Fact]
    public void TenantContext_Binding_AppliesOnlyWhileHeld_AndOnlyOnce()
    {
        var ctx = ContextWith(null);
        var a = Guid.NewGuid();
        Assert.Null(ctx.TenantId);

        using (ctx.BindJobTenant(a))
        {
            Assert.Equal(a, ctx.TenantId);
            Assert.Throws<InvalidOperationException>(() => ctx.BindJobTenant(Guid.NewGuid()));
        }

        Assert.Null(ctx.TenantId);
        Assert.Throws<ArgumentException>(() => ctx.BindJobTenant(Guid.Empty));
    }

    [Fact]
    public void TenantContext_Binding_RefusedOnStaffRequestWithTenantClaim()
    {
        var own = Guid.NewGuid();
        var ctx = ContextWith(new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", own.ToString())], "jwt")));
        Assert.Throws<InvalidOperationException>(() => ctx.BindJobTenant(Guid.NewGuid()));
        Assert.Equal(own, ctx.TenantId);
    }
}
