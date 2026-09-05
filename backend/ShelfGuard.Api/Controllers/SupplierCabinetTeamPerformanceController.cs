using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.SupplierAnalytics;
using ShelfGuard.Application.Features.SupplierAnalytics.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Supplier cabinet — team performance (TASK-695, Phase 8). A read-only per-employee KPI view
/// (order throughput + timing, on-time delivery, discrepancy-free receiving, chat responsiveness,
/// buyer ratings) over the supplier's own marketplace history. Supplier-internal: the buyer
/// ratings here are NOT the public company rating and are never rolled into it.
///
/// Gated at the class by the <c>marketplace_supplier</c> module (every supplier tenant has it);
/// both actions additionally require the <c>staff_management</c> supplier permission — the manager
/// who manages the team is the one who sees their KPIs.
/// </summary>
[ApiController]
[Route("api/supplier-cabinet")]
[Authorize(Policy = AppPolicies.SupplierCabinet)]
[RequireModule("marketplace_supplier")]
public sealed class SupplierCabinetTeamPerformanceController : ControllerBase
{
    private const int DefaultWindowDays = 30;

    private readonly ISupplierTeamPerformanceService _teamPerformance;

    public SupplierCabinetTeamPerformanceController(ISupplierTeamPerformanceService teamPerformance) =>
        _teamPerformance = teamPerformance;

    /// <summary>
    /// Team KPIs for the given window. <c>from</c>/<c>to</c> are <c>YYYY-MM-DD</c>; both omitted →
    /// the last 30 days. The range is capped at 366 days server-side (a wider request is clamped
    /// and the effective window echoed on the response). Each KPI carries a period-over-period
    /// delta vs the equal-length preceding window.
    /// </summary>
    [HttpGet("team-performance")]
    [ProducesResponseType(typeof(SupplierTeamPerformanceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeamPerformance(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.StaffManagement))
            return Forbid();

        var resolvedTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var resolvedFrom = from ?? resolvedTo.AddDays(-(DefaultWindowDays - 1));

        var result = await _teamPerformance.GetAsync(tenantId.Value, resolvedFrom, resolvedTo, ct);
        return Ok(result);
    }

    /// <summary>
    /// The individual buyer reviews (with comments) for one staff member, newest first — the
    /// feedback behind the aggregate. Empty when the employee has no ratings.
    /// </summary>
    [HttpGet("team/{userId:guid}/reviews")]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierEmployeeReviewDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmployeeReviews(Guid userId, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.StaffManagement))
            return Forbid();

        var result = await _teamPerformance.GetEmployeeReviewsAsync(tenantId.Value, userId, ct);
        return Ok(result);
    }

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
