using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Supplier Marketplace — public listing and authenticated review submission.
/// Public endpoints are explicitly [AllowAnonymous] so that unauthenticated
/// callers can browse the marketplace.
/// Module gate [RequireModule("marketplace")] is applied to all endpoints except
/// the public-listing ones (public discovery must remain open even without a
/// tenant context / module activation).
/// </summary>
[ApiController]
[Route("api/marketplace")]
public sealed class MarketplaceController : ControllerBase
{
    private readonly IMarketplaceService _marketplace;

    public MarketplaceController(IMarketplaceService marketplace) => _marketplace = marketplace;

    // ── Public listing (no auth) ──────────────────────────────────────────────

    /// <summary>Paginated public supplier listing, filterable by region/category/plan.</summary>
    [HttpGet("suppliers")]
    [AllowAnonymous]
    [RequireModule("marketplace")]
    [ProducesResponseType(typeof(PagedResult<SupplierListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSuppliers(
        [FromQuery] string? region,
        [FromQuery] string? category,
        [FromQuery] string? plan,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var result = await _marketplace.GetPublicSuppliersAsync(region, category, plan, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>
    /// Full supplier profile. Premium fields returned only when plan=premium or caller is
    /// authenticated.
    /// </summary>
    [HttpGet("suppliers/{id:guid}")]
    [AllowAnonymous]
    [RequireModule("marketplace")]
    [ProducesResponseType(typeof(SupplierProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSupplierById(Guid id, CancellationToken ct)
    {
        bool authenticated = User.Identity?.IsAuthenticated == true;
        var profile = await _marketplace.GetSupplierProfileAsync(id, authenticated, ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>Supplier's item catalog.</summary>
    [HttpGet("suppliers/{id:guid}/items")]
    [AllowAnonymous]
    [RequireModule("marketplace")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSupplierItems(Guid id, CancellationToken ct)
    {
        var items = await _marketplace.GetSupplierItemsAsync(id, ct);
        return Ok(items);
    }

    /// <summary>Search suppliers by item name and optional region.</summary>
    [HttpPost("search")]
    [AllowAnonymous]
    [RequireModule("marketplace")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromBody] SupplierSearchDto request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ItemName))
            return BadRequest(new { error = "ItemName is required." });

        var results = await _marketplace.SearchSuppliersAsync(request, ct);
        return Ok(results);
    }

    // ── Authenticated — leave a review ────────────────────────────────────────

    /// <summary>Leave a review for a supplier. One review per tenant per supplier.</summary>
    [HttpPost("suppliers/{id:guid}/reviews")]
    [Authorize]
    [RequireModule("marketplace")]
    [ProducesResponseType(typeof(SupplierReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateReview(
        Guid id,
        [FromBody] SupplierReviewCreateDto request,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (review, error, isDuplicate) =
            await _marketplace.CreateReviewAsync(id, tenantId.Value, request, ct);

        if (error is not null)
            return isDuplicate ? Conflict(new { error }) : BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, review);
    }

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
