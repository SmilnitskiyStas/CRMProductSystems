using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Transfers;
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

    public async Task<(List<StockTransfer> Items, int Total)> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize, CancellationToken ct = default)
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
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t =>
                (t.FromStore != null && EF.Functions.ILike(t.FromStore.Name, $"%{term}%")) ||
                (t.ToStore != null && EF.Functions.ILike(t.ToStore.Name, $"%{term}%")) ||
                (t.TransferType != null && EF.Functions.ILike(t.TransferType, $"%{term}%")));
        }

        var total = await query.CountAsync(ct);
        query = ApplySort(query, sortBy, sortDescending);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    private static IQueryable<StockTransfer> ApplySort(
        IQueryable<StockTransfer> query, string? sortBy, bool? sortDescending)
    {
        var key = TransferSortKeys.Normalize(sortBy);
        var descending = sortDescending ?? true; // newest-first is the pre-existing default

        return (key, descending) switch
        {
            ("status", false) => query.OrderBy(t => t.Status),
            ("status", true) => query.OrderByDescending(t => t.Status),
            ("from", false) => query.OrderBy(t => t.FromStore != null ? t.FromStore.Name : null),
            ("from", true) => query.OrderByDescending(t => t.FromStore != null ? t.FromStore.Name : null),
            ("to", false) => query.OrderBy(t => t.ToStore != null ? t.ToStore.Name : null),
            ("to", true) => query.OrderByDescending(t => t.ToStore != null ? t.ToStore.Name : null),
            (_, false) => query.OrderBy(t => t.CreatedAt),
            (_, true) => query.OrderByDescending(t => t.CreatedAt),
        };
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
