using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// B2B marketplace orders (TASK-316). Two-tenant tenant_isolation RLS on
/// both marketplace_orders and marketplace_order_items (items carry
/// denormalised copies of the order's tenant pair).
/// </summary>
public sealed class MarketplaceOrderRepository : IMarketplaceOrderRepository
{
    private readonly AppDbContext _db;

    public MarketplaceOrderRepository(AppDbContext db) => _db = db;

    // Phase 3 (plan D4): Items.Batches is included everywhere an order is projected to a DTO —
    // both parties may read it (the supplier through marketplace_order_item_batches'
    // tenant_isolation, the client through the inverted client_read FOR SELECT policy).
    public Task<MarketplaceOrder?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.MarketplaceOrders
            .Include(o => o.Items).ThenInclude(i => i.Batches)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<MarketplaceOrder>> ListForSupplierAsync(
        Guid supplierTenantId, CancellationToken ct = default) =>
        await _db.MarketplaceOrders.AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Batches)
            .Where(o => o.SupplierTenantId == supplierTenantId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MarketplaceOrder>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default) =>
        await _db.MarketplaceOrders.AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Batches)
            .Where(o => o.ClientTenantId == clientTenantId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    /// <summary>
    /// TASK-586: "no receipt exists" OR "receipt exists but still draft" — since receiving
    /// always sets order.Status = Delivered on finalize, Status == Shipped alone already implies
    /// no completed receipt exists; the NOT EXISTS below is the defensive, literal form of the
    /// spec (also covers a receipt row in "draft" some other request left dangling).
    /// </summary>
    public async Task<IReadOnlyList<MarketplaceOrder>> ListAwaitingReceiptForClientAsync(
        Guid clientTenantId, CancellationToken ct = default) =>
        await _db.MarketplaceOrders.AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Batches)
            .Where(o => o.ClientTenantId == clientTenantId && o.Status == MarketplaceOrderStatus.Shipped)
            .Where(o => !_db.MarketplaceOrderReceipts.Any(
                r => r.MarketplaceOrderId == o.Id && r.Status != "draft"))
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(MarketplaceOrder order, CancellationToken ct = default) =>
        await _db.MarketplaceOrders.AddAsync(order, ct);

    public void Update(MarketplaceOrder order) =>
        _db.MarketplaceOrders.Update(order);

    public async Task AddOrderItemBatchAsync(
        MarketplaceOrderItemBatch batch, CancellationToken ct = default) =>
        await _db.MarketplaceOrderItemBatches.AddAsync(batch, ct);

    public Task<int> CountForSupplierAsync(Guid supplierTenantId, CancellationToken ct = default) =>
        _db.MarketplaceOrders.CountAsync(o => o.SupplierTenantId == supplierTenantId, ct);

    // Phase 6a: a cancelled order is never an actionable "new order", so it is excluded on both
    // the null-marker (never opened) and the since-marker paths.
    public Task<int> CountUnseenForSupplierAsync(
        Guid supplierTenantId, DateTimeOffset? since, CancellationToken ct = default)
    {
        var query = _db.MarketplaceOrders.AsNoTracking()
            .Where(o => o.SupplierTenantId == supplierTenantId
                     && o.Status != MarketplaceOrderStatus.Cancelled);

        if (since is not null)
            query = query.Where(o => o.CreatedAt > since.Value);

        return query.CountAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
