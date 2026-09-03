using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class OrderCalcRepository : IOrderCalcRepository
{
    private readonly AppDbContext _db;

    public OrderCalcRepository(AppDbContext db) => _db = db;

    public Task<List<ProductBuffer>> GetBuffersAsync(Guid storeId, CancellationToken ct = default) =>
        _db.ProductBuffers
            .Include(b => b.Product)
            .Where(b => b.StoreId == storeId)
            .ToListAsync(ct);

    public async Task<Dictionary<Guid, decimal>> GetStockOnHandAsync(
        Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken ct = default)
    {
        return await _db.ProductStocks
            .Where(s => s.StoreId == storeId && productIds.Contains(s.ProductId) && s.Quantity > 0)
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(s => s.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);
    }

    public async Task<Dictionary<Guid, decimal>> GetInTransitAsync(
        Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken ct = default)
    {
        // Draft receipts = ordered but not yet received into stock.
        return await _db.StockReceipts
            .Where(r => r.DestinationStoreId == storeId && r.Status == "draft")
            .SelectMany(r => r.Items)
            .Where(i => productIds.Contains(i.ProductId))
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(i => i.QuantityOrdered) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);
    }

    // Phase 4 (plan D5): open B2B marketplace orders the buyer has placed but not yet received —
    // invisible to the replenishment engine until now, which is the "double order" bug.
    private static readonly string[] OpenMarketplaceStatuses = ["new", "confirmed", "shipped"];

    public async Task<Dictionary<Guid, decimal>> GetOpenMarketplaceInTransitAsync(
        Guid storeId, IReadOnlyCollection<Guid> productIds, Guid tenantId, CancellationToken ct = default)
    {
        // marketplace_order_items ⋈ marketplace_orders ⋈ items.
        //
        // Tenant scoping: `items` carries the canonical `tenant_isolation` RLS policy
        // (FixFailOpenTenantIsolationOnReset lists "items"), and this runs on the buyer's own
        // staff session, so ambient RLS already limits the join to the buyer tenant's catalog.
        // The explicit `it.TenantId == tenantId` filter below is kept anyway: this is a
        // cross-tenant marketplace join, the predicate is cheap and index-backed
        // (idx_items_tenant_category_segment_active leads on TenantId), and it keeps the query
        // correct if it is ever reached from a bypass/worker session where `items` RLS is off.
        // `marketplace_orders`' own RLS is OR-based on Supplier/Client tenant, so the buyer
        // session already only sees orders it is a party to — no override needed (plan D5).
        //
        // Unit gate (plan п.2): `oi.Unit` is the string snapshot taken at order time
        // (MarketplaceOrderItem.Unit, nullable); `it.Unit` is the buyer catalog unit. When they
        // differ the line is dropped rather than summed in a foreign unit — a documented v1
        // limitation that trades completeness for never producing a wildly wrong number.
        return await (
                from oi in _db.MarketplaceOrderItems
                join o in _db.MarketplaceOrders on oi.OrderId equals o.Id
                join it in _db.Items on oi.SupplierItemId equals it.SourceSupplierItemId
                where o.DestinationStoreId == storeId
                      && OpenMarketplaceStatuses.Contains(o.Status)
                      && it.TenantId == tenantId
                      && productIds.Contains(it.Id)
                      && oi.SupplierItemId != null
                      && oi.Unit == it.Unit
                select new { ItemId = it.Id, oi.Qty })
            .GroupBy(x => x.ItemId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Qty) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);
    }

    public async Task<Dictionary<Guid, (decimal Moq, decimal Usq)>> GetMoqUsqAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default)
    {
        var settings = await _db.Set<ProductSupplierSetting>()
            .Where(s => productIds.Contains(s.ProductId) && s.IsActive)
            .OrderByDescending(s => s.IsPrimary)
            .ToListAsync(ct);

        return settings
            .GroupBy(s => s.ProductId)
            .ToDictionary(g => g.Key, g => (g.First().Moq, g.First().Usq));
    }

    public Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default) =>
        _db.Locations.AnyAsync(s => s.Id == storeId && s.IsActive, ct);
}
