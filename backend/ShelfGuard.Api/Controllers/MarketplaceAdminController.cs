using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Platform-level marketplace admin endpoints.
/// Accessible only to users with the "provider" role (ProviderOnly policy).
/// These endpoints manage supplier records on behalf of the platform,
/// not on behalf of any tenant.
/// </summary>
[ApiController]
[Route("api/admin/marketplace")]
[Authorize(Policy = AppPolicies.ProviderOnly)]
public sealed class MarketplaceAdminController : ControllerBase
{
    private readonly IMarketplaceService _marketplace;

    public MarketplaceAdminController(IMarketplaceService marketplace)
    {
        _marketplace = marketplace;
    }

    /// <summary>Creates a new supplier and its marketplace profile.</summary>
    [HttpPost("suppliers")]
    [ProducesResponseType(typeof(SupplierProfileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSupplier(
        [FromBody] AdminCreateSupplierDto request,
        CancellationToken ct)
    {
        var (profile, error) = await _marketplace.AdminCreateSupplierAsync(request, ct);

        if (error is not null)
            return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, profile);
    }

    /// <summary>Adds an item to a supplier's catalog.</summary>
    [HttpPost("suppliers/{id:guid}/items")]
    [ProducesResponseType(typeof(SupplierItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddSupplierItem(
        Guid id,
        [FromBody] AdminAddSupplierItemDto request,
        CancellationToken ct)
    {
        var (item, error) = await _marketplace.AdminAddSupplierItemAsync(id, request, ct);

        if (error == "Supplier not found.")
            return NotFound(new { error });

        if (error is not null)
            return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, item);
    }

    /// <summary>Removes an item from a supplier's catalog.</summary>
    [HttpDelete("suppliers/{id:guid}/items/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSupplierItem(
        Guid id,
        Guid itemId,
        CancellationToken ct)
    {
        var error = await _marketplace.AdminDeleteSupplierItemAsync(id, itemId, ct);

        if (error is not null)
            return NotFound(new { error });

        return NoContent();
    }
}
