using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Application.Features.Orders.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderCalcService _orders;
    private readonly IOrderTrackingService _tracking;

    public OrdersController(IOrderCalcService orders, IOrderTrackingService tracking)
    {
        _orders = orders;
        _tracking = tracking;
    }

    // ADR-020 (TASK-346): AtLeastStoreManager unchanged, OR's "orders.manage" capability.
    [HttpPost("calculate")]
    [Authorize(Policy = AppPolicies.OrdersManageOrCapability)]
    [ProducesResponseType(typeof(OrderCalcResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Calculate(CalculateOrderRequest request, CancellationToken ct)
    {
        var (result, error) = await _orders.CalculateAsync(request.StoreId, ct);
        if (error is not null)
            return NotFound(new { error });

        return Ok(result);
    }

    // ── Order tracking: where are my orders now? ─────────────────────────────────────────────────

    /// <summary>
    /// The buyer's purchase orders from both sources (internal supplier receipts + B2B marketplace
    /// orders) with a unified status and timeline. <c>status</c> may repeat
    /// (new|confirmed|in_transit|delivered|cancelled); <c>source</c> is receipt | marketplace.
    /// </summary>
    [HttpGet("tracking")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(OrderTrackingPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTracking(
        [FromQuery] string? source,
        [FromQuery] string[]? status,
        [FromQuery] Guid? store_id,
        [FromQuery] string? search,
        [FromQuery] bool overdue = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        return Ok(await _tracking.GetPagedAsync(tenantId.Value, source, status, store_id, search, page, pageSize, overdue, ct));
    }

    [HttpGet("tracking/summary")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(OrderTrackingSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrackingSummary([FromQuery] Guid? store_id, CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        return Ok(await _tracking.GetSummaryAsync(tenantId.Value, store_id, ct));
    }

    private Guid? GetTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
