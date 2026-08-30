using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Receipts;
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
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize,
        Guid? categoryId = null, int? minItems = null, int? maxItems = null,
        CancellationToken ct = default)
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
        // TASK-640: category_id/min_items/max_items filters for the frontend table filter UI.
        // `.HasValue` checks (never a truthy/non-zero check) — 0 is a valid items-count bound.
        if (categoryId.HasValue)
            query = query.Where(r => r.Items.Any(i => i.Product != null && i.Product.CategoryId == categoryId));
        if (minItems.HasValue)
            query = query.Where(r => r.Items.Count >= minItems);
        if (maxItems.HasValue)
            query = query.Where(r => r.Items.Count <= maxItems);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r =>
                (r.Supplier != null && EF.Functions.ILike(r.Supplier.Name, $"%{term}%")) ||
                (r.DestinationStore != null && EF.Functions.ILike(r.DestinationStore.Name, $"%{term}%")));
        }

        var total = await query.CountAsync(ct);
        query = ApplySort(query, sortBy, sortDescending);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    private static IQueryable<StockReceipt> ApplySort(
        IQueryable<StockReceipt> query, string? sortBy, bool? sortDescending)
    {
        var key = ReceiptSortKeys.Normalize(sortBy);
        var descending = sortDescending ?? true; // newest-first is the pre-existing default

        return (key, descending) switch
        {
            ("status", false) => query.OrderBy(r => r.Status),
            ("status", true) => query.OrderByDescending(r => r.Status),
            ("supplier", false) => query.OrderBy(r => r.Supplier != null ? r.Supplier.Name : null),
            ("supplier", true) => query.OrderByDescending(r => r.Supplier != null ? r.Supplier.Name : null),
            ("destination", false) => query.OrderBy(r => r.DestinationStore != null ? r.DestinationStore.Name : null),
            ("destination", true) => query.OrderByDescending(r => r.DestinationStore != null ? r.DestinationStore.Name : null),
            ("expectedat", false) => query.OrderBy(r => r.ExpectedAt),
            ("expectedat", true) => query.OrderByDescending(r => r.ExpectedAt),
            (_, false) => query.OrderBy(r => r.CreatedAt),
            (_, true) => query.OrderByDescending(r => r.CreatedAt),
        };
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
