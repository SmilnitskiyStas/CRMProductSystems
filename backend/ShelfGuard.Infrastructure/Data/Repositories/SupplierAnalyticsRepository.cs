using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.SupplierAnalytics;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Supplier demand analytics (Phase 6b). Runs on the supplier's own RLS session;
/// <c>marketplace_orders</c> / <c>marketplace_order_items</c> RLS is OR-based so a supplier sees
/// its own orders, and the explicit <c>SupplierTenantId</c> predicate below is defence-in-depth.
/// </summary>
public sealed class SupplierAnalyticsRepository : ISupplierAnalyticsRepository
{
    private readonly AppDbContext _db;

    public SupplierAnalyticsRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SupplierOrderLineRow>> GetOrderLinesAsync(
        Guid supplierTenantId, DateOnly from, DateOnly toInclusive, CancellationToken ct = default)
    {
        var fromDt = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toExclusive = new DateTimeOffset(toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return await (
            from oi in _db.MarketplaceOrderItems.AsNoTracking()
            join o in _db.MarketplaceOrders.AsNoTracking() on oi.OrderId equals o.Id
            where oi.SupplierTenantId == supplierTenantId
               && o.Status != MarketplaceOrderStatus.Cancelled
               && o.CreatedAt >= fromDt
               && o.CreatedAt < toExclusive
            select new SupplierOrderLineRow(
                o.Id,
                oi.SupplierItemId,
                oi.ItemName,
                oi.ClientTenantId,
                oi.Qty,
                oi.LineTotal,
                o.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SupplierCatalogRow>> GetAvailableCatalogAsync(
        Guid supplierTenantId, CancellationToken ct = default) =>
        await _db.SupplierItems.AsNoTracking()
            .Where(i => i.TenantId == supplierTenantId && i.IsAvailable)
            .Select(i => new SupplierCatalogRow(
                i.Id,
                i.CustomName ?? (i.Item != null ? i.Item.Name : string.Empty)))
            .ToListAsync(ct);
}
