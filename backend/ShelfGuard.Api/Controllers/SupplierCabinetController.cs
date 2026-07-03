using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Self-service supplier cabinet (v4.1, ADR-016, TASK-284).
/// Available only to supplier_admin users of supplier tenants with the
/// marketplace_supplier module. Every operation is scoped to the calling
/// tenant's owner-managed supplier — no supplier id is ever accepted from
/// the client.
/// </summary>
[ApiController]
[Route("api/supplier-cabinet")]
[Authorize(Policy = AppPolicies.SupplierCabinet)]
[RequireModule("marketplace_supplier")]
public sealed class SupplierCabinetController : ControllerBase
{
    private readonly ISupplierCabinetService _cabinet;

    public SupplierCabinetController(ISupplierCabinetService cabinet) => _cabinet = cabinet;

    // ── Profile ───────────────────────────────────────────────────────────────

    /// <summary>Own supplier profile with metrics.</summary>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(SupplierProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (profile, error) = await _cabinet.GetProfileAsync(tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(profile);
    }

    /// <summary>Patch-updates the own profile (region, categories, website, delivery regions, working hours, payment terms).</summary>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(SupplierProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] CabinetProfileUpdateDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (profile, error) = await _cabinet.UpdateProfileAsync(tenantId.Value, request, ct);
        return error is not null ? NotFound(new { error }) : Ok(profile);
    }

    /// <summary>Toggles marketplace visibility (IsPublic) of the own profile.</summary>
    [HttpPost("profile/publish")]
    [ProducesResponseType(typeof(SupplierProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TogglePublish(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (profile, error) = await _cabinet.TogglePublishAsync(tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(profile);
    }

    // ── Items ─────────────────────────────────────────────────────────────────

    /// <summary>Own item catalog including unavailable items.</summary>
    [HttpGet("items")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetItems(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (items, error) = await _cabinet.GetItemsAsync(tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(items);
    }

    /// <summary>Adds an item to the own catalog.</summary>
    [HttpPost("items")]
    [ProducesResponseType(typeof(SupplierItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddItem(
        [FromBody] AdminAddSupplierItemDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (item, error) = await _cabinet.AddItemAsync(tenantId.Value, request, ct);

        if (error == SupplierCabinetService.CabinetNotAvailableError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, item);
    }

    /// <summary>Patch-updates an item of the own catalog.</summary>
    [HttpPut("items/{id:guid}")]
    [ProducesResponseType(typeof(SupplierItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateItem(
        Guid id, [FromBody] AdminUpdateSupplierItemDto request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (item, error) = await _cabinet.UpdateItemAsync(tenantId.Value, id, request, ct);

        if (error == SupplierCabinetService.CabinetNotAvailableError || error == "Item not found.")
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });

        return Ok(item);
    }

    /// <summary>Removes an item from the own catalog.</summary>
    [HttpDelete("items/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteItem(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var error = await _cabinet.DeleteItemAsync(tenantId.Value, id, ct);
        return error is not null ? NotFound(new { error }) : NoContent();
    }

    // ── Reviews / metrics (read-only) ─────────────────────────────────────────

    /// <summary>Reviews left for the own supplier (read-only, paginated).</summary>
    [HttpGet("reviews")]
    [ProducesResponseType(typeof(PagedResult<PublicSupplierReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReviews(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var (reviews, error) = await _cabinet.GetReviewsAsync(tenantId.Value, page, pageSize, ct);
        return error is not null ? NotFound(new { error }) : Ok(reviews);
    }

    /// <summary>Aggregated metrics of the own supplier (read-only).</summary>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(SupplierMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMetrics(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (metrics, error) = await _cabinet.GetMetricsAsync(tenantId.Value, ct);
        return error is not null ? NotFound(new { error }) : Ok(metrics);
    }

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
