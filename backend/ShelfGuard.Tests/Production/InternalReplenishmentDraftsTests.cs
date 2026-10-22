using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Receipts.Dtos;
using Xunit;

namespace ShelfGuard.Tests.Production;

/// <summary>
/// TASK-755: POST /api/internal/replenishment-drafts/run - status mapping. The shared-secret
/// InternalJobKeyFilter is class-level on InternalJobsController and covered exhaustively in
/// InternalProductionDemandTests; here we prove the new action is behind the same controller
/// (so behind that filter) and maps the runner result faithfully.
/// </summary>
public sealed class InternalReplenishmentDraftsTests
{
    private readonly IAutoReplenishmentTenantRunner _runner = Substitute.For<IAutoReplenishmentTenantRunner>();

    private InternalJobsController Controller() =>
        new(Substitute.For<IProductionDemandTenantRunner>(), _runner)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public void NewEndpoint_LivesOnTheKeyProtectedController()
    {
        var type = typeof(InternalJobsController);
        Assert.Contains(type.GetCustomAttributes(true), a => a is TypeFilterAttribute f
            && f.ImplementationType == typeof(ShelfGuard.Api.Infrastructure.InternalJobKeyFilter));
        Assert.NotNull(type.GetMethod(nameof(InternalJobsController.RunReplenishmentDrafts)));
    }

    [Fact]
    public async Task EmptyTenant_400_RunnerNotCalled()
    {
        var result = await Controller().RunReplenishmentDrafts(new(Guid.Empty), default);
        Assert.IsType<BadRequestObjectResult>(result);
        await _runner.DidNotReceiveWithAnyArgs().RunAsync(default, default);
    }

    [Fact]
    public async Task NullBody_400()
    {
        Assert.IsType<BadRequestObjectResult>(await Controller().RunReplenishmentDrafts(null, default));
    }

    [Fact]
    public async Task UnknownTenant_404()
    {
        var id = Guid.NewGuid();
        _runner.RunAsync(id, Arg.Any<CancellationToken>())
            .Returns(new AutoReplenishmentRunResult(AutoReplenishmentRunStatus.TenantNotFound, [], "Tenant not found."));
        Assert.IsType<NotFoundObjectResult>(await Controller().RunReplenishmentDrafts(new(id), default));
    }

    [Fact]
    public async Task Skipped_422()
    {
        var id = Guid.NewGuid();
        _runner.RunAsync(id, Arg.Any<CancellationToken>())
            .Returns(new AutoReplenishmentRunResult(AutoReplenishmentRunStatus.Skipped, [], "Tenant is inactive."));
        var result = Assert.IsType<UnprocessableEntityObjectResult>(await Controller().RunReplenishmentDrafts(new(id), default));
        Assert.Equal("skipped", Assert.IsType<InternalJobsController.ReplenishmentRunResponse>(result.Value).Status);
    }

    [Fact]
    public async Task Completed_200_RunsOnlyRequestedTenant()
    {
        var id = Guid.NewGuid();
        var draft = new ReceiptDto(Guid.NewGuid(), null, "Постачальник", Guid.NewGuid(), "Магазин", false, "draft", null, null,
            null, DateTime.UtcNow,
            [new(Guid.NewGuid(), Guid.NewGuid(), "Молоко", null, 5m, null, null, null, null, null, false)],
            Source: "auto_replenishment");
        _runner.RunAsync(id, Arg.Any<CancellationToken>())
            .Returns(new AutoReplenishmentRunResult(AutoReplenishmentRunStatus.Completed, [draft]));

        var ok = Assert.IsType<OkObjectResult>(await Controller().RunReplenishmentDrafts(new(id), default));
        var body = Assert.IsType<InternalJobsController.ReplenishmentRunResponse>(ok.Value);
        Assert.Equal("completed", body.Status);
        var d = Assert.Single(body.Drafts);
        Assert.Equal(1, d.LineCount);
        Assert.Equal("Постачальник", d.SupplierName);
        await _runner.Received(1).RunAsync(id, Arg.Any<CancellationToken>());
        await _runner.DidNotReceive().RunAsync(Arg.Is<Guid>(g => g != id), Arg.Any<CancellationToken>());
    }
}
