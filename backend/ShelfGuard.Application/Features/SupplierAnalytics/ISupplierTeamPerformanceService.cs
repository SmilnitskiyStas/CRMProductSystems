using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.SupplierAnalytics.Dtos;

namespace ShelfGuard.Application.Features.SupplierAnalytics;

/// <summary>
/// Supplier team performance (TASK-695, Phase 8) — a read-only per-employee KPI view over the
/// supplier's own marketplace order history, chat threads and buyer→employee ratings. Gated at
/// the controller by the <c>marketplace_supplier</c> module + the <c>staff_management</c> supplier
/// permission (the manager who manages the team sees their KPIs). Supplier-internal: the buyer
/// ratings here are NOT the public company rating.
/// </summary>
public interface ISupplierTeamPerformanceService
{
    /// <summary>
    /// Team KPIs for <paramref name="supplierTenantId"/> over <c>[from, to]</c> (inclusive).
    /// <paramref name="from"/> after <paramref name="to"/> is swapped; a window wider than 366
    /// days is clamped by moving <c>from</c> forward (the effective window is echoed on the DTO).
    /// One row per current staff member (a user with no activity in the window still appears, all
    /// zeroes/nulls).
    /// </summary>
    Task<SupplierTeamPerformanceDto> GetAsync(
        Guid supplierTenantId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// The individual buyer reviews (with comments) for one staff member, newest first — so the
    /// manager can read the feedback behind the aggregate.
    /// </summary>
    Task<IReadOnlyList<SupplierEmployeeReviewDetailDto>> GetEmployeeReviewsAsync(
        Guid supplierTenantId, Guid supplierUserId, CancellationToken ct = default);
}
