using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Schedules;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/schedules")]
[Authorize]
public sealed class SchedulesController : ControllerBase
{
    private readonly IScheduleService _schedules;

    public SchedulesController(IScheduleService schedules) => _schedules = schedules;

    // ── Schedules ────────────────────────────────────────────────────────────

    [HttpGet]
    [ProducesResponseType(typeof(List<WorkScheduleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? locationId,
        [FromQuery] DateOnly? weekStart,
        CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var result = await _schedules.GetSchedulesAsync(tenantId.Value, locationId, weekStart, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkScheduleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var schedule = await _schedules.GetByIdAsync(id, tenantId.Value, ct);
        return schedule is null ? NotFound() : Ok(schedule);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.SchedulesManageOrCapability)]
    [ProducesResponseType(typeof(WorkScheduleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateWorkScheduleDto dto, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var (schedule, error) = await _schedules.CreateScheduleAsync(tenantId.Value, userId.Value, dto, ct);
        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = schedule!.Id }, schedule);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.SchedulesManageOrCapability)]
    [ProducesResponseType(typeof(WorkScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateWorkScheduleDto dto, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (schedule, error) = await _schedules.UpdateScheduleAsync(id, tenantId.Value, dto, ct);
        if (error == "Schedule not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(schedule);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.SchedulesManageOrCapability)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var deleted = await _schedules.DeleteScheduleAsync(id, tenantId.Value, ct);
        return deleted ? NoContent() : NotFound();
    }

    // ── Shifts ───────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/shifts")]
    [Authorize(Policy = AppPolicies.SchedulesManageOrCapability)]
    [ProducesResponseType(typeof(ScheduleShiftDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddShift(Guid id, CreateShiftDto dto, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (shift, error) = await _schedules.AddShiftAsync(id, tenantId.Value, dto, ct);
        if (error == "Schedule not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, shift);
    }

    [HttpPut("{scheduleId:guid}/shifts/{shiftId:guid}")]
    [Authorize(Policy = AppPolicies.SchedulesManageOrCapability)]
    [ProducesResponseType(typeof(ScheduleShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateShift(Guid scheduleId, Guid shiftId, UpdateShiftDto dto, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (shift, error) = await _schedules.UpdateShiftAsync(scheduleId, shiftId, tenantId.Value, dto, ct);
        if (error == "Shift not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(shift);
    }

    [HttpDelete("{scheduleId:guid}/shifts/{shiftId:guid}")]
    [Authorize(Policy = AppPolicies.SchedulesManageOrCapability)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteShift(Guid scheduleId, Guid shiftId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var deleted = await _schedules.DeleteShiftAsync(scheduleId, shiftId, tenantId.Value, ct);
        return deleted ? NoContent() : NotFound();
    }

    // ── My Shifts ────────────────────────────────────────────────────────────

    [HttpGet("my-shifts")]
    [ProducesResponseType(typeof(List<ScheduleShiftDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyShifts(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var shifts = await _schedules.GetMyShiftsAsync(userId.Value, tenantId.Value, from, to, ct);
        return Ok(shifts);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
}
