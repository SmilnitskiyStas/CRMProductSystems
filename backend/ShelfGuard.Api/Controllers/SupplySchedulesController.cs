using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.SupplySchedules;
using ShelfGuard.Application.Features.SupplySchedules.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/supply-schedules")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class SupplySchedulesController : ControllerBase
{
    private readonly ISupplyScheduleService _schedules;

    public SupplySchedulesController(ISupplyScheduleService schedules) => _schedules = schedules;

    [HttpGet]
    [ProducesResponseType(typeof(List<SupplyScheduleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid? store_id,
        [FromQuery] Guid? supplier_id,
        CancellationToken ct)
    {
        var schedules = await _schedules.GetAsync(store_id, supplier_id, ct);
        return Ok(schedules);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SupplyScheduleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateSupplyScheduleRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (schedule, error) = await _schedules.CreateAsync(tenantId.Value, request, ct);
        if (error is not null)
            return MapError(error);

        return Created($"/api/supply-schedules/{schedule!.Id}", schedule);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SupplyScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, UpdateSupplyScheduleRequest request, CancellationToken ct)
    {
        var (schedule, error) = await _schedules.UpdateAsync(id, request, ct);
        if (error is not null)
            return MapError(error);

        return Ok(schedule);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var error = await _schedules.DeleteAsync(id, ct);
        if (error is not null)
            return NotFound(new { error });

        return NoContent();
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private IActionResult MapError(string error) => error switch
    {
        _ when error.EndsWith("not found.") => NotFound(new { error }),
        _ when error.Contains("already exists") => Conflict(new { error }),
        _ => BadRequest(new { error }),
    };

    private Guid? GetTenantId()
    {
        var tenantIdStr = User.FindFirstValue("tenant_id");
        return Guid.TryParse(tenantIdStr, out var id) && id != Guid.Empty ? id : null;
    }
}
