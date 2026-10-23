using NSubstitute;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Production;

public sealed class AccessMatrixProductionTests
{
    [Theory]
    [InlineData("Done")]
    [InlineData("InProgress")]
    [InlineData("Cancelled")]
    [InlineData("Planned")]
    public async Task Generic_update_cannot_change_status_or_perform_stock_writes(string status)
    {
        var repo = Substitute.For<IProductionRepository>(); var order = new ProductionOrder { Status = ProductionOrderStatus.Planned };
        repo.GetOrderByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var result = await new ProductionService(repo, NSubstitute.Substitute.For<ShelfGuard.Application.Features.Production.IProductionOrderSyncService>()).UpdateOrderAsync(order.Id, new ProductionOrderUpdateDto(status, null));
        Assert.Equal(400, result.StatusCode); Assert.Equal(ProductionOrderStatus.Planned, order.Status);
        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await repo.DidNotReceive().AddStockAsync(Arg.Any<ProductStock>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Planned_order_must_be_started_before_completion()
    {
        var repo = Substitute.For<IProductionRepository>(); var order = new ProductionOrder { Status = ProductionOrderStatus.Planned };
        repo.GetOrderByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var svc = new ProductionService(repo, NSubstitute.Substitute.For<ShelfGuard.Application.Features.Production.IProductionOrderSyncService>());
        Assert.Equal(409, (await svc.CompleteOrderAsync(order.Id, Guid.NewGuid(), Guid.NewGuid())).StatusCode);
        Assert.Null((await svc.StartOrderAsync(order.Id)).Error);
        Assert.Equal(ProductionOrderStatus.InProgress, order.Status); Assert.NotNull(order.StartedAt);
        Assert.Equal(409, (await svc.StartOrderAsync(order.Id)).StatusCode);
    }
    [Fact]
    public async Task Completed_order_is_immutable_through_generic_edit_and_cancel()
    {
        var repo = Substitute.For<IProductionRepository>(); var order = new ProductionOrder { Status = ProductionOrderStatus.Done };
        repo.GetOrderByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var svc = new ProductionService(repo, NSubstitute.Substitute.For<ShelfGuard.Application.Features.Production.IProductionOrderSyncService>());
        Assert.Equal(409, (await svc.UpdateOrderAsync(order.Id, new ProductionOrderUpdateDto(null, "Changed"))).StatusCode);
        Assert.Equal(409, (await svc.CancelOrderAsync(order.Id)).StatusCode);
        Assert.Equal(409, (await svc.StartOrderAsync(order.Id)).StatusCode);
        await repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
