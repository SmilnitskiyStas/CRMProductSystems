using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.IoT;
using ShelfGuard.Application.Features.IoT.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/iot")]
[Authorize(Policy = AppPolicies.CanViewStock)]
public sealed class IotController : ControllerBase
{
    private readonly IIotDeviceService _iot;

    public IotController(IIotDeviceService iot) => _iot = iot;

    // ── Devices ────────────────────────────────────────────────────────────

    [HttpGet("devices")]
    [ProducesResponseType(typeof(List<IotDeviceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDevices([FromQuery(Name = "store_id")] Guid? storeId, CancellationToken ct)
    {
        var devices = await _iot.GetAllAsync(storeId, ct);
        return Ok(devices);
    }

    [HttpGet("devices/{id:guid}")]
    [ProducesResponseType(typeof(IotDeviceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDevice(Guid id, CancellationToken ct)
    {
        var device = await _iot.GetByIdAsync(id, ct);
        return device is null ? NotFound() : Ok(device);
    }

    [HttpPost("devices")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(IotDeviceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterDeviceRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null)
            return Forbid();

        var (device, error) = await _iot.RegisterAsync(tenantId.Value, request, ct);
        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetDevice), new { id = device!.Id }, device);
    }

    [HttpPut("devices/{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(IotDeviceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateDeviceRequest request, CancellationToken ct)
    {
        var (device, error) = await _iot.UpdateAsync(id, request, ct);

        if (error == "Device not found.")
            return NotFound();

        if (error is not null)
            return BadRequest(new { error });

        return Ok(device);
    }

    [HttpDelete("devices/{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var (success, _) = await _iot.DeactivateAsync(id, ct);
        return success ? NoContent() : NotFound();
    }

    // ── Readings ───────────────────────────────────────────────────────────

    [HttpGet("devices/{id:guid}/readings")]
    [ProducesResponseType(typeof(List<TemperatureReadingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReadings(
        Guid id, [FromQuery] int hours = 24, [FromQuery] int limit = 500, CancellationToken ct = default)
    {
        var (readings, error) = await _iot.GetTemperatureReadingsAsync(id, hours, limit, ct);
        return error is not null ? NotFound() : Ok(readings);
    }

    [HttpGet("temperature")]
    [ProducesResponseType(typeof(List<LatestTemperatureDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatestTemperatures(
        [FromQuery(Name = "store_id")] Guid storeId, CancellationToken ct)
    {
        var latest = await _iot.GetLatestTemperaturesAsync(storeId, ct);
        return Ok(latest);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
