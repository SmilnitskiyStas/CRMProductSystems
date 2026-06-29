using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Features.Stock.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/stock")]
[Authorize(Policy = AppPolicies.CanViewStock)]
public sealed class StockController : ControllerBase
{
    private readonly IStockService _stock;

    public StockController(IStockService stock) => _stock = stock;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductStockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? store_id,
        [FromQuery] string? status,
        [FromQuery] Guid? zone_id,
        [FromQuery] Guid? product_id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = new PagedQuery { Page = page, PageSize = pageSize };
        var result = await _stock.GetPagedAsync(store_id, status, zone_id, product_id, query.ClampedPage, query.ClampedPageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var batch = await _stock.GetByIdAsync(id, ct);
        return batch is null ? NotFound() : Ok(batch);
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(ShelfGuard.Application.Features.Stock.Dtos.StockSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary([FromQuery] Guid? store_id, CancellationToken ct)
    {
        var summary = await _stock.GetSummaryAsync(store_id, ct);
        return Ok(summary);
    }

    [HttpGet("zones-summary")]
    [ProducesResponseType(typeof(List<ShelfGuard.Application.Features.Stock.Dtos.ZoneSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetZonesSummary([FromQuery] Guid? store_id, CancellationToken ct)
    {
        var zones = await _stock.GetZonesSummaryAsync(store_id, ct);
        return Ok(zones);
    }

    [HttpGet("expiring")]
    [ProducesResponseType(typeof(List<ProductStockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExpiring(
        [FromQuery] Guid? store_id,
        [FromQuery] int days = 7,
        CancellationToken ct = default)
    {
        if (days < 1 || days > 365)
            return BadRequest(new { error = "days must be between 1 and 365." });

        var batches = await _stock.GetExpiringAsync(store_id, days, ct);
        return Ok(batches);
    }

    [HttpGet("expired")]
    [ProducesResponseType(typeof(List<ProductStockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExpired(
        [FromQuery] Guid? store_id,
        CancellationToken ct)
    {
        var batches = await _stock.GetExpiredAsync(store_id, ct);
        return Ok(batches);
    }

    [HttpGet("needs-check")]
    [ProducesResponseType(typeof(List<ProductStockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNeedsCheck(
        [FromQuery] Guid? store_id,
        CancellationToken ct)
    {
        var batches = await _stock.GetNeedsCheckAsync(store_id, ct);
        return Ok(batches);
    }

    [HttpGet("suggestions")]
    [ProducesResponseType(typeof(List<SuggestionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSuggestions(
        [FromQuery] Guid? store_id,
        CancellationToken ct)
    {
        var suggestions = await _stock.GetSuggestionsAsync(store_id, ct);
        return Ok(suggestions);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ProductStockDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateStockRequest request, CancellationToken ct)
    {
        var (tenantId, performedBy) = GetContext();
        if (tenantId is null || performedBy is null)
            return Forbid();

        var (stock, error) = await _stock.CreateAsync(tenantId.Value, performedBy.Value, request, ct);
        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = stock!.Id }, stock);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ProductStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateStockRequest request, CancellationToken ct)
    {
        var (stock, error) = await _stock.UpdateAsync(id, request, ct);

        if (error == "Stock batch not found.")
            return NotFound();

        if (error is not null)
            return BadRequest(new { error });

        return Ok(stock);
    }

    [HttpPost("{id:guid}/verify")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(ProductStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Verify(Guid id, CancellationToken ct)
    {
        var (_, performedBy) = GetContext();
        if (performedBy is null)
            return Forbid();

        var (stock, error) = await _stock.VerifyAsync(id, performedBy.Value, ct);

        if (error == "Stock batch not found.")
            return NotFound();

        return Ok(stock);
    }

    [HttpPost("fefo-consume")]
    [Authorize(Policy = AppPolicies.CanReceiveStock)]
    [ProducesResponseType(typeof(FefoConsumeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FefoConsume(FefoConsumeRequest request, CancellationToken ct)
    {
        var (tenantId, performedBy) = GetContext();
        if (tenantId is null || performedBy is null)
            return Forbid();

        var result = await _stock.FefoConsumeAsync(tenantId.Value, performedBy.Value, request, ct);

        if (result.Error is not null && result.QuantityConsumed == 0)
            return BadRequest(new { error = result.Error });

        return Ok(result);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private (Guid? TenantId, Guid? UserId) GetContext()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;

        Guid.TryParse(userIdStr, out var userId);
        Guid.TryParse(tenantIdStr, out var tenantId);

        return (
            tenantId == Guid.Empty ? null : tenantId,
            userId == Guid.Empty ? null : userId
        );
    }
}
