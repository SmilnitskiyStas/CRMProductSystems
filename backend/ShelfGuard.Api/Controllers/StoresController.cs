using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Stores;
using ShelfGuard.Application.Features.Stores.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/stores")]
[Authorize(Policy = AppPolicies.CanViewStock)]
public sealed class StoresController : ControllerBase
{
    private readonly IStoreService _stores;

    public StoresController(IStoreService stores) => _stores = stores;

    // ── Stores ─────────────────────────────────────────────────────────────

    [HttpGet]
    [ProducesResponseType(typeof(List<StoreDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var stores = await _stores.GetAllAsync(ct);
        return Ok(stores);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(StoreDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var store = await _stores.GetByIdAsync(id, ct);
        return store is null ? NotFound() : Ok(store);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
    [ProducesResponseType(typeof(StoreDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateStoreRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null)
            return Forbid();

        var (store, error) = await _stores.CreateAsync(tenantId.Value, request, ct);
        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = store!.Id }, store);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
    [ProducesResponseType(typeof(StoreDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateStoreRequest request, CancellationToken ct)
    {
        var (store, error) = await _stores.UpdateAsync(id, request, ct);

        if (error == "Store not found.")
            return NotFound();

        if (error is not null)
            return BadRequest(new { error });

        return Ok(store);
    }

    [HttpPut("{id:guid}/floor-plan")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(StoreDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateFloorPlan(Guid id, UpdateFloorPlanRequest request, CancellationToken ct)
    {
        var (store, error) = await _stores.UpdateFloorPlanAsync(id, request, ct);

        if (error == "Store not found.")
            return NotFound();

        if (error is not null)
            return BadRequest(new { error });

        return Ok(store);
    }

    // ── Zones ──────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/zones")]
    [ProducesResponseType(typeof(List<StoreZoneDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetZones(Guid id, CancellationToken ct)
    {
        var zones = await _stores.GetZonesAsync(id, ct);
        return Ok(zones);
    }

    [HttpPost("{id:guid}/zones")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(StoreZoneDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateZone(Guid id, CreateZoneRequest request, CancellationToken ct)
    {
        var (zone, error) = await _stores.CreateZoneAsync(id, request, ct);

        if (error == "Store not found.")
            return NotFound(new { error });

        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetZones), new { id }, zone);
    }

    [HttpPut("{id:guid}/zones/{zoneId:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(StoreZoneDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateZone(Guid id, Guid zoneId, UpdateZoneRequest request, CancellationToken ct)
    {
        var (zone, error) = await _stores.UpdateZoneAsync(id, zoneId, request, ct);

        if (error == "Zone not found.")
            return NotFound();

        if (error is not null)
            return BadRequest(new { error });

        return Ok(zone);
    }

    [HttpDelete("{id:guid}/zones/{zoneId:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteZone(Guid id, Guid zoneId, CancellationToken ct)
    {
        var (success, error) = await _stores.DeleteZoneAsync(id, zoneId, ct);
        return success ? NoContent() : NotFound();
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
