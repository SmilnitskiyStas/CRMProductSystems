using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly IShelfService _shelves;

    public LocationsController(ILocationService locations, IShelfService shelves)
    {
        _locations = locations;
        _shelves = shelves;
    }

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

    /// <summary>
    /// Resolves a free-text address to coordinates (OpenStreetMap Nominatim). The location form
    /// calls this on a button click and sends the returned lat/lon back via the normal update.
    /// </summary>
    [HttpPost("geocode")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(GeocodeAddressResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Geocode(GeocodeAddressRequest request, CancellationToken ct)
    {
        var result = await _locations.GeocodeAddressAsync(request.Query, ct);
        return result is null
            ? NotFound(new { error = "Не вдалося визначити координати за адресою." })
            : Ok(result);
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

    // ── Shelf planogram + AI photo check ───────────────────────────────────

    [HttpGet("{id:guid}/zones/{zoneId:guid}/shelves")]
    [ProducesResponseType(typeof(List<ShelfSectionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShelves(Guid id, Guid zoneId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (sections, error) = await _shelves.GetShelvesAsync(tenantId.Value, id, zoneId, ct);
        return error is null ? Ok(sections) : NotFound(new { error });
    }

    [HttpGet("{id:guid}/zones/{zoneId:guid}/shelf-items")]
    [ProducesResponseType(typeof(List<ShelfItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShelfItems(Guid id, Guid zoneId, [FromQuery] Guid? shelfId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (items, error) = await _shelves.GetShelfItemsAsync(tenantId.Value, id, zoneId, shelfId, ct);
        return error is null ? Ok(items) : NotFound(new { error });
    }

    [HttpPut("{id:guid}/zones/{zoneId:guid}/shelf-items")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(List<ShelfItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetShelfItems(Guid id, Guid zoneId, SetShelfItemsRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (items, error) = await _shelves.SetShelfItemsAsync(tenantId.Value, id, zoneId, request, ct);
        if (error == "Zone not found.") return NotFound(new { error });
        return error is null ? Ok(items) : BadRequest(new { error });
    }

    [HttpPost("{id:guid}/zones/{zoneId:guid}/scans")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [EnableRateLimiting("shelf-scan-ai")]
    [RequestSizeLimit(ShelfService.MaxImageBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(ShelfScanDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateScan(
        Guid id, Guid zoneId, IFormFile file, [FromForm] Guid? shelfId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file uploaded." });

        await using var stream = file.OpenReadStream();
        var (scan, error, status) = await _shelves.CreateScanAsync(
            tenantId.Value, GetUserId(), id, zoneId, shelfId, stream, file.Length, file.FileName, ct);

        if (error is not null) return StatusCode(status ?? 400, new { error });
        return CreatedAtAction(nameof(GetScan), new { id, zoneId, scanId = scan!.Id }, scan);
    }

    [HttpGet("{id:guid}/zones/{zoneId:guid}/scans")]
    [ProducesResponseType(typeof(List<ShelfScanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetScans(Guid id, Guid zoneId, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (scans, error) = await _shelves.GetScansAsync(tenantId.Value, id, zoneId, limit, ct);
        return error is null ? Ok(scans) : NotFound(new { error });
    }

    [HttpGet("{id:guid}/zones/{zoneId:guid}/scans/{scanId:guid}")]
    [ProducesResponseType(typeof(ShelfScanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetScan(Guid id, Guid zoneId, Guid scanId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (scan, error) = await _shelves.GetScanAsync(tenantId.Value, id, zoneId, scanId, ct);
        return error is null ? Ok(scan) : NotFound(new { error });
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
