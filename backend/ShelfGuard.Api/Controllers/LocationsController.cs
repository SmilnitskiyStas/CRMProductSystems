using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Application.Features.Locations.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/locations")]
[Authorize(Policy = AppPolicies.CanViewStock)]
public sealed class LocationsController : ControllerBase
{
    private readonly ILocationService _locations;

    public LocationsController(ILocationService locations) => _locations = locations;

    // ── Locations ──────────────────────────────────────────────────────────

    [HttpGet]
    [ProducesResponseType(typeof(List<LocationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        // TASK-401: scoped roles get the list narrowed to their user_locations assignments.
        var locations = await _locations.GetAllAsync(GetTenantId(), GetUserId(), GetRole(), ct);
        return Ok(locations);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(LocationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var location = await _locations.GetByIdAsync(id, ct);
        return location is null ? NotFound() : Ok(location);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
    [ProducesResponseType(typeof(LocationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateLocationRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null)
            return Forbid();

        var (location, error) = await _locations.CreateAsync(tenantId.Value, request, ct);
        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = location!.Id }, location);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
    [ProducesResponseType(typeof(LocationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateLocationRequest request, CancellationToken ct)
    {
        var (location, error) = await _locations.UpdateAsync(id, request, ct);

        if (error == "Location not found.")
            return NotFound();

        if (error is not null)
            return BadRequest(new { error });

        return Ok(location);
    }

    [HttpPut("{id:guid}/floor-plan")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(LocationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateFloorPlan(Guid id, UpdateFloorPlanRequest request, CancellationToken ct)
    {
        var (location, error) = await _locations.UpdateFloorPlanAsync(id, request, ct);

        if (error == "Location not found.")
            return NotFound();

        if (error is not null)
            return BadRequest(new { error });

        return Ok(location);
    }

    // ── Zones ──────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/zones")]
    [ProducesResponseType(typeof(List<LocationZoneDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetZones(Guid id, CancellationToken ct)
    {
        var zones = await _locations.GetZonesAsync(id, ct);
        return Ok(zones);
    }

    [HttpPost("{id:guid}/zones")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(LocationZoneDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateZone(Guid id, CreateZoneRequest request, CancellationToken ct)
    {
        var (zone, error) = await _locations.CreateZoneAsync(id, request, ct);

        if (error == "Location not found.")
            return NotFound(new { error });

        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetZones), new { id }, zone);
    }

    [HttpPut("{id:guid}/zones/{zoneId:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(LocationZoneDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateZone(Guid id, Guid zoneId, UpdateZoneRequest request, CancellationToken ct)
    {
        var (zone, error) = await _locations.UpdateZoneAsync(id, zoneId, request, ct);

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
        var (success, error) = await _locations.DeleteZoneAsync(id, zoneId, ct);
        return success ? NoContent() : NotFound();
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private string? GetRole() =>
        User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
}
