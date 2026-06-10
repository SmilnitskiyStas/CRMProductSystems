using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Discounts;
using ShelfGuard.Application.Features.Discounts.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Manages product discounts within a tenant.
/// POST → pending → PUT approve → active → (auto-expire or PUT cancel).
/// Only store managers and above can create / approve / cancel discounts.
/// </summary>
[ApiController]
[Route("api/discounts")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class DiscountsController : ControllerBase
{
    private readonly IDiscountService _service;

    public DiscountsController(IDiscountService service) => _service = service;

    /// <summary>Returns all discounts for the current tenant. Supports ?storeId and ?status filters.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DiscountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid?   storeId = null,
        [FromQuery] string? status  = null,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var query  = new DiscountListQuery(storeId, status);
        var result = await _service.GetAllAsync(tenantId.Value, query, ct);
        return Ok(result);
    }

    /// <summary>Returns a single discount by id.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DiscountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (discount, error) = await _service.GetByIdAsync(tenantId.Value, id, ct);
        return discount is null ? NotFound(new { error }) : Ok(discount);
    }

    /// <summary>Creates a new discount in 'pending' status.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(DiscountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDiscountRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = ResolveUserId();
        if (userId is null) return Forbid();

        var (discount, error) = await _service.CreateAsync(tenantId.Value, userId.Value, request, ct);
        if (discount is null) return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = discount.Id }, discount);
    }

    /// <summary>Approves a pending discount — transitions it to 'active'.</summary>
    [HttpPut("{id:guid}/approve")]
    [ProducesResponseType(typeof(DiscountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = ResolveUserId();
        if (userId is null) return Forbid();

        var (discount, error) = await _service.ApproveAsync(tenantId.Value, id, userId.Value, ct);
        if (discount is null)
            return error!.Contains("not found")
                ? NotFound(new { error })
                : BadRequest(new { error });

        return Ok(discount);
    }

    /// <summary>Cancels a pending or active discount.</summary>
    [HttpPut("{id:guid}/cancel")]
    [ProducesResponseType(typeof(DiscountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (discount, error) = await _service.CancelAsync(tenantId.Value, id, ct);
        if (discount is null)
            return error!.Contains("not found")
                ? NotFound(new { error })
                : BadRequest(new { error });

        return Ok(discount);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    private Guid? ResolveUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
