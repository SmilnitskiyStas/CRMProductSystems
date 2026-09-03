using ShelfGuard.Application.Features.SupplierAnalytics.Dtos;

namespace ShelfGuard.Application.Features.SupplierAnalytics;

/// <summary>
/// Supplier demand analytics (supplier-portal expansion #7, Phase 6b) — a read-only view over the
/// supplier's own marketplace order history. Gated at the controller by the
/// <c>marketplace_supplier</c> module + the <c>analytics_view</c> supplier permission.
/// </summary>
public interface ISupplierAnalyticsService
{
    /// <summary>
    /// Demand analytics for <paramref name="supplierTenantId"/> over <c>[from, to]</c> (inclusive).
    /// The range is capped at 366 days — a wider request is clamped by moving <c>from</c> forward,
    /// and the effective window is echoed back on the DTO. <paramref name="from"/> after
    /// <paramref name="to"/> is swapped rather than rejected.
    /// </summary>
    Task<SupplierAnalyticsDto> GetAsync(
        Guid supplierTenantId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
