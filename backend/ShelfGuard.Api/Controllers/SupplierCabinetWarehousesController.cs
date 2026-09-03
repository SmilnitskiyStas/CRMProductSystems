using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.SupplierInventory;
using ShelfGuard.Application.Features.SupplierInventory.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Supplier cabinet — own warehouses (supplier-portal expansion Phase 1, plan
/// `1-partitioned-book.md`, decision D1). A warehouse is a Location row of type "warehouse";
/// this controller is the thin supplier-facing wrapper over <see cref="ISupplierWarehouseService"/>
/// (which delegates to ILocationService), kept separate from the retail LocationsController
/// so it doesn't inherit that controller's store-scope / zone / floor-plan surface.
///
/// Gated at the class by the "supplier_inventory" module (provider-granted, default-off);
/// per-action by the "warehouse_management" supplier permission. Every operation is scoped
/// to the calling supplier tenant — no location id from another tenant is accepted.
/// </summary>
[ApiController]
[Route("api/supplier-cabinet/warehouses")]
[Authorize(Policy = AppPolicies.SupplierCabinet)]
[RequireModule("supplier_inventory")]
public sealed class SupplierCabinetWarehousesController : ControllerBase
{
    private readonly ISupplierWarehouseService _warehouses;

    public SupplierCabinetWarehousesController(ISupplierWarehouseService warehouses)
        => _warehouses = warehouses;

    /// <summary>Own warehouses (active and inactive).</summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierWarehouseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WarehouseManagement)) return Forbid();

        var warehouses = await _warehouses.ListAsync(tenantId.Value, ct);
        return Ok(warehouses);
    }

    /// <summary>Creates a new warehouse for the calling supplier tenant.</summary>
    [HttpPost("")]
    [ProducesResponseType(typeof(SupplierWarehouseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateSupplierWarehouseRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WarehouseManagement)) return Forbid();

        var (warehouse, error) = await _warehouses.CreateAsync(tenantId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });
        return StatusCode(StatusCodes.Status201Created, warehouse);
    }

    /// <summary>Updates a warehouse of the calling supplier tenant.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SupplierWarehouseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSupplierWarehouseRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WarehouseManagement)) return Forbid();

        var (warehouse, error) = await _warehouses.UpdateAsync(tenantId.Value, id, request, ct);
        if (error == SupplierWarehouseService.WarehouseNotFoundError)
            return NotFound(new { error });
        if (error is not null)
            return BadRequest(new { error });
        return Ok(warehouse);
    }

    /// <summary>Soft-deactivates a warehouse of the calling supplier tenant.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WarehouseManagement)) return Forbid();

        var (success, error) = await _warehouses.DeactivateAsync(tenantId.Value, id, ct);
        if (success) return NoContent();
        return error == SupplierWarehouseService.WarehouseNotFoundError
            ? NotFound(new { error })
            : BadRequest(new { error });
    }

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
