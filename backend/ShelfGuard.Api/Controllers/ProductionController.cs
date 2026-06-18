using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Production Module — Recipes and Production Orders.
/// All endpoints require authentication and the "production" module.
/// Thin controller: zero business logic — delegates entirely to IProductionService.
/// </summary>
[ApiController]
[Route("api/production")]
[Authorize]
[RequireModule("production")]
public sealed class ProductionController : ControllerBase
{
    private readonly IProductionService _svc;

    public ProductionController(IProductionService svc) => _svc = svc;

    // ─────────────────────────────────────────────────────────────────────────
    // Recipes
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("recipes")]
    [ProducesResponseType(typeof(List<RecipeListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecipes(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var result = await _svc.GetRecipesAsync(includeInactive, ct);
        return Ok(result);
    }

    [HttpGet("recipes/{id:guid}")]
    [ProducesResponseType(typeof(RecipeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecipeById(Guid id, CancellationToken ct)
    {
        var result = await _svc.GetRecipeByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("recipes")]
    [ProducesResponseType(typeof(RecipeDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRecipe(
        [FromBody] RecipeCreateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { error = "Name is required." });

        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (recipe, error, statusCode) = await _svc.CreateRecipeAsync(dto, tenantId.Value, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return StatusCode(StatusCodes.Status201Created, recipe);
    }

    [HttpPut("recipes/{id:guid}")]
    [ProducesResponseType(typeof(RecipeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRecipe(
        Guid id, [FromBody] RecipeUpdateDto dto, CancellationToken ct)
    {
        var (recipe, error, statusCode) = await _svc.UpdateRecipeAsync(id, dto, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return Ok(recipe);
    }

    [HttpDelete("recipes/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteRecipe(Guid id, CancellationToken ct)
    {
        var (ok, error, statusCode) = await _svc.DeactivateRecipeAsync(id, ct);
        if (!ok) return StatusCode(statusCode!.Value, new { error });
        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Production Orders
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("orders")]
    [ProducesResponseType(typeof(List<ProductionOrderListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders(
        [FromQuery] string? status,
        [FromQuery] Guid? recipeId,
        [FromQuery] Guid? locationId,
        CancellationToken ct)
    {
        var result = await _svc.GetOrdersAsync(status, recipeId, locationId, ct);
        return Ok(result);
    }

    [HttpGet("orders/{id:guid}")]
    [ProducesResponseType(typeof(ProductionOrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById(Guid id, CancellationToken ct)
    {
        var result = await _svc.GetOrderByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("orders")]
    [ProducesResponseType(typeof(ProductionOrderDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateOrder(
        [FromBody] ProductionOrderCreateDto dto, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId   = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var (order, error, statusCode) = await _svc.CreateOrderAsync(dto, tenantId.Value, userId.Value, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return StatusCode(StatusCodes.Status201Created, order);
    }

    [HttpPut("orders/{id:guid}")]
    [ProducesResponseType(typeof(ProductionOrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateOrder(
        Guid id, [FromBody] ProductionOrderUpdateDto dto, CancellationToken ct)
    {
        var (order, error, statusCode) = await _svc.UpdateOrderAsync(id, dto, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return Ok(order);
    }

    [HttpPost("orders/{id:guid}/complete")]
    [ProducesResponseType(typeof(ProductionCompleteResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(object), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CompleteOrder(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId   = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var (order, error, statusCode, insufficientItemId, outputStockBatchId) =
            await _svc.CompleteOrderAsync(id, tenantId.Value, userId.Value, ct);

        if (error is not null)
        {
            // For insufficient stock (422), include itemId in the response
            if (statusCode == 422 && insufficientItemId.HasValue)
                return StatusCode(422, new { error, itemId = insufficientItemId.Value });
            return StatusCode(statusCode!.Value, new { error });
        }

        var result = new ProductionCompleteResultDto(
            order!.Id,
            outputStockBatchId!.Value,
            order.PlannedQty,
            order.RecipeName);

        return Ok(result);
    }

    [HttpPost("orders/{id:guid}/cancel")]
    [ProducesResponseType(typeof(ProductionOrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelOrder(Guid id, CancellationToken ct)
    {
        var (order, error, statusCode) = await _svc.CancelOrderAsync(id, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return Ok(order);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    private Guid? ResolveUserId()
    {
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
