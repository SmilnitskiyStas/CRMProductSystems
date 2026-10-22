using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class MovementRepository : IMovementRepository
{
    private readonly AppDbContext _db;

    public MovementRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<StockMovement>> GetAsync(
        Guid tenantId,
        Guid? productId,
        Guid? storeId,
        string? type,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = BuildQuery(tenantId, productId, storeId, type, from, to);

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(
        Guid tenantId,
        Guid? productId,
        Guid? storeId,
        string? type,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        return BuildQuery(tenantId, productId, storeId, type, from, to).CountAsync(ct);
    }

    private static readonly string[] OpenMarketplaceStatuses = ["new", "confirmed", "shipped"];

    public async Task<IReadOnlyList<IncomingSupplyRow>> GetIncomingAsync(
        Guid tenantId, Guid productId, Guid? storeId, CancellationToken ct = default)
    {
        var receiptRows = await (
                from r in _db.StockReceipts
                from i in r.Items
                where r.TenantId == tenantId
                      && (r.Status == "draft" || r.Status == "ordered")
                      && i.ProductId == productId
                      && (!storeId.HasValue || r.DestinationStoreId == storeId)
                select new
                {
                    r.Id,
                    r.Status,
                    Qty = i.QuantityOrdered,
                    r.CreatedAt,
                    r.ExpectedAt,
                    Supplier = r.Supplier != null ? r.Supplier.Name : null,
                    r.DestinationStoreId,
                    Store = r.DestinationStore != null ? r.DestinationStore.Name : null,
                })
            .ToListAsync(ct);

        var marketplaceRows = await (
                from oi in _db.MarketplaceOrderItems
                join o in _db.MarketplaceOrders on oi.OrderId equals o.Id
                join it in _db.Items on oi.SupplierItemId equals it.SourceSupplierItemId
                where it.Id == productId
                      && it.TenantId == tenantId
                      && o.ClientTenantId == tenantId
                      && OpenMarketplaceStatuses.Contains(o.Status)
                      && (!storeId.HasValue || o.DestinationStoreId == storeId)
                      && oi.Unit == it.Unit
                select new
                {
                    o.Id,
                    o.OrderNumber,
                    o.Status,
                    oi.Qty,
                    oi.Unit,
                    o.CreatedAt,
                    o.ExpectedDeliveryDate,
                    o.ShippedAt,
                    o.EstimatedDeliveryDays,
                    o.SupplierTenantId,
                    o.DestinationStoreId,
                })
            .ToListAsync(ct);

        var supplierIds = marketplaceRows.Select(m => m.SupplierTenantId).Distinct().ToList();
        var supplierNames = supplierIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Tenants.Where(t => supplierIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var storeIds = marketplaceRows.Where(m => m.DestinationStoreId.HasValue)
            .Select(m => m.DestinationStoreId!.Value).Distinct().ToList();
        var storeNames = storeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Locations.Where(l => storeIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        var result = new List<IncomingSupplyRow>();
        result.AddRange(receiptRows.Select(r => new IncomingSupplyRow(
            "receipt", r.Id, null, r.Status, r.Qty, null, r.CreatedAt, r.ExpectedAt, null,
            r.Supplier, r.DestinationStoreId, r.Store)));
        result.AddRange(marketplaceRows.Select(m =>
        {
            // The supplier-set date wins; otherwise estimate from the ship date + estimated days.
            DateOnly? expected = m.ExpectedDeliveryDate
                ?? (m.ShippedAt.HasValue && m.EstimatedDeliveryDays.HasValue
                    ? DateOnly.FromDateTime(m.ShippedAt.Value.UtcDateTime.AddDays(m.EstimatedDeliveryDays.Value))
                    : null);
            return new IncomingSupplyRow(
                "marketplace", m.Id, m.OrderNumber, m.Status, m.Qty, m.Unit,
                m.CreatedAt.UtcDateTime, expected, m.ShippedAt?.UtcDateTime,
                supplierNames.GetValueOrDefault(m.SupplierTenantId),
                m.DestinationStoreId,
                m.DestinationStoreId.HasValue ? storeNames.GetValueOrDefault(m.DestinationStoreId.Value) : null);
        }));
        return result;
    }

    public async Task<Dictionary<Guid, string>> GetStoreNamesAsync(
        IReadOnlyCollection<Guid> storeIds, CancellationToken ct = default)
    {
        if (storeIds.Count == 0) return new Dictionary<Guid, string>();
        return await _db.Locations
            .Where(l => storeIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);
    }

    public async Task<Dictionary<Guid, string>> GetUserNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, string>();
        return await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    public async Task<Dictionary<Guid, (string? BatchNumber, DateOnly ExpiryDate)>> GetBatchesAsync(
        IReadOnlyCollection<Guid> productStockIds, CancellationToken ct = default)
    {
        if (productStockIds.Count == 0)
            return new Dictionary<Guid, (string? BatchNumber, DateOnly ExpiryDate)>();
        var rows = await _db.ProductStocks
            .Where(s => productStockIds.Contains(s.Id))
            .Select(s => new { s.Id, s.BatchNumber, s.ExpiryDate })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => (r.BatchNumber, r.ExpiryDate));
    }

    private IQueryable<StockMovement> BuildQuery(
        Guid tenantId,
        Guid? productId,
        Guid? storeId,
        string? type,
        DateOnly? from,
        DateOnly? to)
    {
        var query = _db.StockMovements
            .Where(m => m.TenantId == tenantId)
            .AsQueryable();

        if (productId.HasValue)
            query = query.Where(m => m.ProductId == productId);

        if (storeId.HasValue)
            query = query.Where(m => m.FromStoreId == storeId || m.ToStoreId == storeId);

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(m => m.MovementType == type);

        if (from.HasValue)
        {
            var fromUtc = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(m => m.CreatedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(m => m.CreatedAt <= toUtc);
        }

        return query;
    }
}
