using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Features.Stock.Dtos;
using ShelfGuard.Application.Features.Transfers;
using Xunit;

namespace ShelfGuard.Tests.Stock;

public sealed class InventoryMutationControllerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Adjustment_uses_the_authenticated_actor_and_rejects_a_missing_actor(bool hasActor)
    {
        var actor = Guid.NewGuid();
        var stock = Substitute.For<IStockService>();
        var controller = new StockController(stock) { ControllerContext = Context(hasActor ? actor : null) };
        var id = Guid.NewGuid();
        var request = new UpdateStockRequest(null, null, null, 5m, DateOnly.FromDateTime(DateTime.UtcNow));
        var result = await controller.Update(id, request, CancellationToken.None);
        if (hasActor)
        {
            Assert.IsType<OkObjectResult>(result);
            await stock.Received(1).UpdateAsync(id, actor, request, CancellationToken.None);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
            Assert.Empty(stock.ReceivedCalls());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_uses_the_authenticated_actor_and_rejects_a_missing_actor(bool hasActor)
    {
        var actor = Guid.NewGuid();
        var transfers = Substitute.For<ITransferService>();
        var controller = new TransfersController(transfers) { ControllerContext = Context(hasActor ? actor : null) };
        var id = Guid.NewGuid();
        var result = await controller.Cancel(id, CancellationToken.None);
        if (hasActor)
        {
            Assert.IsType<OkObjectResult>(result);
            await transfers.Received(1).CancelAsync(id, actor, CancellationToken.None);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
            Assert.Empty(transfers.ReceivedCalls());
        }
    }

    private static ControllerContext Context(Guid? actor) => new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            actor.HasValue ? [new Claim(ClaimTypes.NameIdentifier, actor.ToString()!)] : [], "test")) }
    };
}
