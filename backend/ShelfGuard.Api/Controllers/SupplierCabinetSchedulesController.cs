using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Schedules;
using ShelfGuard.Application.Features.Users.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Self-service supplier cabinet — employee work schedules for the supplier's own warehouses
/// (supplier-portal expansion Phase 5, plan <c>1-partitioned-book.md</c> D6, request #6).
///
/// Thin pass-through to the shared <see cref="IScheduleService"/> (the same service the retail
/// <c>SchedulesController</c> wraps) with the supplier tenant id resolved from the JWT — exactly
/// mirrors how <c>SupplierCabinetController.InviteStaffAsync</c> delegates into <c>IUserService</c>.
/// <see cref="IScheduleService"/> is already tenant-parametrised and validates
/// <c>LocationExistsAsync(locationId, tenantId)</c>, so a supplier can only attach schedules/shifts
/// to its own warehouses (Location rows of type "warehouse"). No schedule-service change, no
/// migration — <c>work_schedules</c>/<c>schedule_shifts</c> RLS is tenant_isolation + provider_bypass
/// + worker_bypass with NO store_scope, so a supplier tenant sees only its own rows.
///
/// Gating: role (supplier_admin) via the controller policy, provider-granted module
/// <c>supplier_workforce</c> (default-off), and the per-action supplier permission
/// <c>workforce_management</c> on every mutation. GET list/detail/my-shifts and the assignee
/// picker are open to any supplier_admin of a tenant with the module (mirrors the retail
/// controller, where GET is not behind <c>SchedulesManageOrCapability</c>).
/// </summary>
[ApiController]
[Route("api/supplier-cabinet/schedules")]
[Authorize(Policy = AppPolicies.SupplierCabinet)]
[RequireModule("supplier_workforce")]
public sealed class SupplierCabinetSchedulesController : ControllerBase
{
    private readonly IScheduleService _schedules;
    private readonly ISupplierCabinetService _cabinet;

    public SupplierCabinetSchedulesController(IScheduleService schedules, ISupplierCabinetService cabinet)
    {
        _schedules = schedules;
        _cabinet   = cabinet;
    }

    // ── Schedules ────────────────────────────────────────────────────────────

    /// <summary>Own tenant's work schedules, optionally filtered by warehouse / week.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<WorkScheduleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? locationId,
        [FromQuery] DateOnly? weekStart,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var result = await _schedules.GetSchedulesAsync(tenantId.Value, locationId, weekStart, ct);
        return Ok(result);
    }

    /// <summary>One work schedule with its shifts.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkScheduleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var schedule = await _schedules.GetByIdAsync(id, tenantId.Value, ct);
        return schedule is null ? NotFound() : Ok(schedule);
    }

    /// <summary>Creates a weekly work schedule for one of the own tenant's warehouses.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkScheduleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateWorkScheduleDto dto, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WorkforceManagement)) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var (schedule, error) = await _schedules.CreateScheduleAsync(tenantId.Value, userId.Value, dto, ct);
        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = schedule!.Id }, schedule);
    }

    /// <summary>Renames a schedule / changes its status (draft → published → archived).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WorkScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateWorkScheduleDto dto, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WorkforceManagement)) return Forbid();

        var (schedule, error) = await _schedules.UpdateScheduleAsync(id, tenantId.Value, dto, ct);
        if (error == "Schedule not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(schedule);
    }

    /// <summary>Deletes a schedule and its shifts.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WorkforceManagement)) return Forbid();

        var deleted = await _schedules.DeleteScheduleAsync(id, tenantId.Value, ct);
        return deleted ? NoContent() : NotFound();
    }

    // ── Shifts ───────────────────────────────────────────────────────────────

    /// <summary>Adds a shift for a staff member to a schedule.</summary>
    [HttpPost("{id:guid}/shifts")]
    [ProducesResponseType(typeof(ScheduleShiftDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddShift(Guid id, CreateShiftDto dto, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WorkforceManagement)) return Forbid();

        var (shift, error) = await _schedules.AddShiftAsync(id, tenantId.Value, dto, ct);
        if (error == "Schedule not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return StatusCode(StatusCodes.Status201Created, shift);
    }

    /// <summary>Updates a shift's time window / break / role override / status.</summary>
    [HttpPut("{scheduleId:guid}/shifts/{shiftId:guid}")]
    [ProducesResponseType(typeof(ScheduleShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateShift(Guid scheduleId, Guid shiftId, UpdateShiftDto dto, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WorkforceManagement)) return Forbid();

        var (shift, error) = await _schedules.UpdateShiftAsync(scheduleId, shiftId, tenantId.Value, dto, ct);
        if (error == "Shift not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(shift);
    }

    /// <summary>Removes a shift from a schedule.</summary>
    [HttpDelete("{scheduleId:guid}/shifts/{shiftId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteShift(Guid scheduleId, Guid shiftId, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WorkforceManagement)) return Forbid();

        var deleted = await _schedules.DeleteShiftAsync(scheduleId, shiftId, tenantId.Value, ct);
        return deleted ? NoContent() : NotFound();
    }

    // ── My shifts ────────────────────────────────────────────────────────────

    /// <summary>The calling staff member's own shifts in a date range ("Мій розклад").</summary>
    [HttpGet("my-shifts")]
    [ProducesResponseType(typeof(List<ScheduleShiftDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyShifts(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var shifts = await _schedules.GetMyShiftsAsync(userId.Value, tenantId.Value, from, to, ct);
        return Ok(shifts);
    }

    // ── Assignee picker ──────────────────────────────────────────────────────

    /// <summary>
    /// Staff of the own tenant, for the shift-assignee dropdown. Gated by
    /// <c>workforce_management</c> (same as the manage actions) rather than
    /// <c>staff_management</c> — a schedule manager without the team-management permission
    /// still needs the list of people to place on shifts. Delegates to the same
    /// <c>ISupplierCabinetService.GetStaffAsync</c> the "Команда" page uses.
    /// </summary>
    [HttpGet("staff")]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStaff(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WorkforceManagement)) return Forbid();

        var staff = await _cabinet.GetStaffAsync(tenantId.Value, ct);
        return Ok(staff);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
