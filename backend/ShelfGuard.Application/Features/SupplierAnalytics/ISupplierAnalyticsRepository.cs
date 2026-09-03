namespace ShelfGuard.Application.Features.SupplierAnalytics;

/// <summary>
/// Data access for supplier demand analytics (Phase 6b). Every query runs on the supplier's own
/// RLS session and additionally filters on <c>SupplierTenantId</c> explicitly (defence-in-depth) —
/// a supplier can only ever see its own marketplace order history.
/// </summary>
public interface ISupplierAnalyticsRepository
{
    /// <summary>
    /// One row per <c>marketplace_order_items</c> line whose parent order was created in
    /// <c>[from, toInclusive]</c> and is NOT cancelled, for <paramref name="supplierTenantId"/>.
    /// Aggregation (totals, top/slow items, per-buyer, daily trend) happens in the service —
    /// marketplace order volume is low (B2B) so an in-memory roll-up is simpler and safer than a
    /// stack of GROUP BY translations.
    /// </summary>
    Task<IReadOnlyList<SupplierOrderLineRow>> GetOrderLinesAsync(
        Guid supplierTenantId, DateOnly from, DateOnly toInclusive, CancellationToken ct = default);

    /// <summary>
    /// The supplier's currently-available catalog entries (<c>supplier_items</c> where
    /// <c>TenantId == supplierTenantId</c> and <c>IsAvailable</c>) — the LEFT side of the
    /// slow-movers join, so a never-ordered item still shows up with zero demand.
    /// </summary>
    Task<IReadOnlyList<SupplierCatalogRow>> GetAvailableCatalogAsync(
        Guid supplierTenantId, CancellationToken ct = default);
}

/// <summary>Projected marketplace order line for analytics (see <see cref="ISupplierAnalyticsRepository.GetOrderLinesAsync"/>).</summary>
public sealed record SupplierOrderLineRow(
    Guid OrderId,
    Guid? SupplierItemId,
    string ItemName,
    Guid ClientTenantId,
    decimal Qty,
    decimal LineTotal,
    DateTimeOffset OrderCreatedAt);

/// <summary>Projected available supplier catalog entry (see <see cref="ISupplierAnalyticsRepository.GetAvailableCatalogAsync"/>).</summary>
public sealed record SupplierCatalogRow(Guid SupplierItemId, string ItemName);
