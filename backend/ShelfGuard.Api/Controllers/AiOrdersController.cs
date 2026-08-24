using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.AiOrders;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

// ADR-020 (TASK-346): no class-level policy — Get* keep AtLeastStoreManager OR "ai_orders.view",
// Generate/UpdateItem/Accept/Reject keep AtLeastStoreManager OR "ai_orders.manage".
[ApiController]
[Route("api/ai-orders")]
public sealed class AiOrdersController : ControllerBase
{
    private readonly IAiOrderService _aiOrders;

    public AiOrdersController(IAiOrderService aiOrders) => _aiOrders = aiOrders;

    // TASK-610: storeIds is a repeated query param (`?storeIds=guid1&storeIds=guid2`) — omitted
    // or empty means "all stores" (same convention as TASK-608's Analytics/Stock endpoints).
    [HttpGet]
    [Authorize(Policy = AppPolicies.AiOrdersViewOrCapability)]
    [ProducesResponseType(typeof(List<AiOrderListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList([FromQuery] Guid[]? storeIds, CancellationToken ct)
        => Ok(await _aiOrders.GetListAsync(storeIds, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPolicies.AiOrdersViewOrCapability)]
    [ProducesResponseType(typeof(AiOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var (order, error) = await _aiOrders.GetByIdAsync(id, ct);
        return error is not null ? NotFound(new { error }) : Ok(order);
    }

    [HttpPost("generate")]
    [Authorize(Policy = AppPolicies.AiOrdersManageOrCapability)]
    [ProducesResponseType(typeof(AiOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Generate(GenerateAiOrderRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (order, error) = await _aiOrders.GenerateAsync(tenantId.Value, request.StoreId, ct);
        if (error == "Store not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });

        return Ok(order);
    }

    [HttpPut("{id:guid}/items/{itemId:guid}")]
    [Authorize(Policy = AppPolicies.AiOrdersManageOrCapability)]
    [ProducesResponseType(typeof(AiOrderItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateItem(
        Guid id, Guid itemId, UpdateAiOrderItemRequest request, CancellationToken ct)
    {
        var (item, error) = await _aiOrders.UpdateItemAsync(id, itemId, request, ct);
        if (error == "Suggestion item not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(item);
    }

    [HttpPost("{id:guid}/accept")]
    [Authorize(Policy = AppPolicies.AiOrdersManageOrCapability)]
    [ProducesResponseType(typeof(AiOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct)
    {
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var (order, error) = await _aiOrders.AcceptAsync(id, userId, ct);
        if (error == "AI order suggestion not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(order);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AppPolicies.AiOrdersManageOrCapability)]
    [ProducesResponseType(typeof(AiOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        var (order, error) = await _aiOrders.RejectAsync(id, ct);
        if (error == "AI order suggestion not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(order);
    }

    private Guid? GetTenantId()
    {
        Guid.TryParse(User.FindFirstValue("tenant_id"), out var id);
        return id == Guid.Empty ? null : id;
    }
}
