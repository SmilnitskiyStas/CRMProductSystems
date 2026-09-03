using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.SupplierInventory;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D3).
/// Parallel to <see cref="ReceiptRepository"/>. Single-row reads are tracked — the service
/// mutates the loaded receipt / lines in place before <c>SaveChangesAsync</c>.
/// </summary>
public sealed class SupplierStockReceiptRepository : ISupplierStockReceiptRepository
{
    private readonly AppDbContext _db;

    public SupplierStockReceiptRepository(AppDbContext db) => _db = db;

    public async Task<List<SupplierStockReceipt>> ListAsync(
        Guid tenantId, Guid? warehouseId, string? status, CancellationToken ct = default)
    {
        var query = _db.SupplierStockReceipts
            .Include(r => r.Warehouse)
            .Include(r => r.Items).ThenInclude(i => i.SupplierItem).ThenInclude(si => si!.Item)
            .Where(r => r.TenantId == tenantId);

        if (warehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        return await query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
    }

    public Task<SupplierStockReceipt?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _db.SupplierStockReceipts
            .Include(r => r.Warehouse)
            .Include(r => r.Items).ThenInclude(i => i.SupplierItem).ThenInclude(si => si!.Item)
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

    public async Task AddAsync(SupplierStockReceipt receipt, CancellationToken ct = default) =>
        await _db.SupplierStockReceipts.AddAsync(receipt, ct);

    public async Task AddStockAsync(SupplierStock stock, CancellationToken ct = default) =>
        await _db.SupplierStocks.AddAsync(stock, ct);

    public async Task AddMovementAsync(SupplierStockMovement movement, CancellationToken ct = default) =>
        await _db.SupplierStockMovements.AddAsync(movement, ct);

    public void Update(SupplierStockReceipt receipt) => _db.SupplierStockReceipts.Update(receipt);

    public void RemoveItem(SupplierStockReceiptItem item) => _db.SupplierStockReceiptItems.Remove(item);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
