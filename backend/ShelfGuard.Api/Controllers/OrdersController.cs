using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Application.Features.Orders.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderCalcService _orders;

    public OrdersController(IOrderCalcService orders) => _orders = orders;

    [HttpPost("calculate")]
    [ProducesResponseType(typeof(OrderCalcResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Calculate(CalculateOrderRequest request, CancellationToken ct)
    {
        var (result, error) = await _orders.CalculateAsync(request.StoreId, ct);
        if (error is not null)
            return NotFound(new { error });

        return Ok(result);
    }
}
