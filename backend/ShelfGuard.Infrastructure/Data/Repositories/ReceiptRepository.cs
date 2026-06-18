using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ReceiptRepository : IReceiptRepository
{
    private readonly AppDbContext _db;

    public ReceiptRepository(AppDbContext db) => _db = db;

    public async Task<List<StockReceipt>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default)
    {
        var query = _db.StockReceipts
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .Include(r => r.Supplier)
            .Include(r => r.DestinationStore)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(r => r.DestinationStoreId == storeId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<(List<StockReceipt> Items, int Total)> GetPagedAsync(
        Guid? storeId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.StockReceipts
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .Include(r => r.Supplier)
            .Include(r => r.DestinationStore)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(r => r.DestinationStoreId == storeId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<StockReceipt?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.StockReceipts
            .Include(r => r.Items).ThenInclude(i => i.Product)
            .Include(r => r.Supplier)
            .Include(r => r.DestinationStore)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<StockReceiptItem?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default) =>
        _db.StockReceiptItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);

    public async Task AddAsync(StockReceipt receipt, CancellationToken ct = default) =>
        await _db.StockReceipts.AddAsync(receipt, ct);

    public async Task AddStockAsync(ProductStock stock, CancellationToken ct = default) =>
        await _db.ProductStocks.AddAsync(stock, ct);

    public async Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) =>
        await _db.StockMovements.AddAsync(movement, ct);

    public void Update(StockReceipt receipt) => _db.StockReceipts.Update(receipt);

    public void UpdateItem(StockReceiptItem item) => _db.StockReceiptItems.Update(item);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
