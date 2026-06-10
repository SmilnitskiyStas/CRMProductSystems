using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class TransferRepository : ITransferRepository
{
    private readonly AppDbContext _db;

    public TransferRepository(AppDbContext db) => _db = db;

    public async Task<List<StockTransfer>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default)
    {
        var query = _db.StockTransfers
            .Include(t => t.Items).ThenInclude(i => i.Product)
            .Include(t => t.FromStore)
            .Include(t => t.ToStore)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(t => t.FromStoreId == storeId || t.ToStoreId == storeId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);

        return await query.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
    }

    public Task<StockTransfer?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.StockTransfers
            .Include(t => t.Items).ThenInclude(i => i.Product)
            .Include(t => t.FromStore)
            .Include(t => t.ToStore)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<ProductStock?> GetStockByIdAsync(Guid stockId, CancellationToken ct = default) =>
        _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == stockId, ct);

    public async Task AddAsync(StockTransfer transfer, CancellationToken ct = default) =>
        await _db.StockTransfers.AddAsync(transfer, ct);

    public async Task AddStockAsync(ProductStock stock, CancellationToken ct = default) =>
        await _db.ProductStocks.AddAsync(stock, ct);

    public async Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) =>
        await _db.StockMovements.AddAsync(movement, ct);

    public void Update(StockTransfer transfer) => _db.StockTransfers.Update(transfer);

    public void UpdateStock(ProductStock stock) => _db.ProductStocks.Update(stock);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
