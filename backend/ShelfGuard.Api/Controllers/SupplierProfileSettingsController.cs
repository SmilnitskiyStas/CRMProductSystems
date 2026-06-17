using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Supplier self-management: a supplier tenant can read and update their own
/// marketplace profile via these endpoints.
/// Routes: GET/PUT /api/settings/supplier-profile
/// </summary>
[ApiController]
[Route("api/settings/supplier-profile")]
[Authorize]
public sealed class SupplierProfileSettingsController : ControllerBase
{
    private readonly IMarketplaceService _marketplace;

    public SupplierProfileSettingsController(IMarketplaceService marketplace) =>
        _marketplace = marketplace;

    /// <summary>Returns the calling tenant's own supplier marketplace profile.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(SupplierProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var profile = await _marketplace.GetOwnProfileAsync(tenantId.Value, ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>
    /// Updates the calling tenant's supplier marketplace profile.
    /// Supports patch semantics — only non-null fields are updated.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(SupplierProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        [FromBody] SupplierProfileUpdateDto request,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (profile, error) = await _marketplace.UpdateOwnProfileAsync(tenantId.Value, request, ct);

        if (error is not null)
        {
            return error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(new { error })
                : BadRequest(new { error });
        }

        return Ok(profile);
    }

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
