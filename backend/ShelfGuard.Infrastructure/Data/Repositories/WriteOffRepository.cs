using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.WriteOffs;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class WriteOffRepository : IWriteOffRepository
{
    private readonly AppDbContext _db;

    public WriteOffRepository(AppDbContext db) => _db = db;

    public async Task<List<WriteOff>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default)
    {
        var query = _db.WriteOffs
            .Include(w => w.Items).ThenInclude(i => i.Product)
            .Include(w => w.Items).ThenInclude(i => i.ProductStock)
            .Include(w => w.Store)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(w => w.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(w => w.Status == status);

        return await query.OrderByDescending(w => w.CreatedAt).ToListAsync(ct);
    }

    public async Task<(List<WriteOff> Items, int Total)> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize,
        Guid? categoryId = null, decimal? minLossAmount = null, decimal? maxLossAmount = null,
        CancellationToken ct = default)
    {
        var query = _db.WriteOffs
            .Include(w => w.Items).ThenInclude(i => i.Product)
            .Include(w => w.Items).ThenInclude(i => i.ProductStock)
            .Include(w => w.Store)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(w => w.StoreId == storeId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(w => w.Status == status);
        // TASK-640: category_id filters by line-item category; min/max_loss_amount range-filter
        // the pre-computed WriteOff.TotalLossAmount column (nullable — a write-off with no total
        // set simply won't match once either bound is applied, via EF's normal null-comparison
        // SQL semantics). `.HasValue` checks (never truthy/non-zero) — 0 is a valid bound.
        if (categoryId.HasValue)
            query = query.Where(w => w.Items.Any(i => i.Product != null && i.Product.CategoryId == categoryId));
        if (minLossAmount.HasValue)
            query = query.Where(w => w.TotalLossAmount >= minLossAmount);
        if (maxLossAmount.HasValue)
            query = query.Where(w => w.TotalLossAmount <= maxLossAmount);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(w =>
                (w.Store != null && EF.Functions.ILike(w.Store.Name, $"%{term}%")) ||
                (w.Reason != null && EF.Functions.ILike(w.Reason, $"%{term}%")));
        }

        var total = await query.CountAsync(ct);
        query = ApplySort(query, sortBy, sortDescending);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    private static IQueryable<WriteOff> ApplySort(
        IQueryable<WriteOff> query, string? sortBy, bool? sortDescending)
    {
        var key = WriteOffSortKeys.Normalize(sortBy);
        var descending = sortDescending ?? true; // newest-first is the pre-existing default

        return (key, descending) switch
        {
            ("status", false) => query.OrderBy(w => w.Status),
            ("status", true) => query.OrderByDescending(w => w.Status),
            ("reason", false) => query.OrderBy(w => w.Reason),
            ("reason", true) => query.OrderByDescending(w => w.Reason),
            ("netloss", false) => query.OrderBy(w => w.TotalLossAmountPurchase.HasValue || w.TotalReimbursementAmount.HasValue
                ? (w.TotalLossAmountPurchase ?? 0m) - (w.TotalReimbursementAmount ?? 0m)
                : (decimal?)null),
            ("netloss", true) => query.OrderByDescending(w => w.TotalLossAmountPurchase.HasValue || w.TotalReimbursementAmount.HasValue
                ? (w.TotalLossAmountPurchase ?? 0m) - (w.TotalReimbursementAmount ?? 0m)
                : (decimal?)null),
            (_, false) => query.OrderBy(w => w.CreatedAt),
            (_, true) => query.OrderByDescending(w => w.CreatedAt),
        };
    }

    public Task<WriteOff?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.WriteOffs
            .Include(w => w.Items).ThenInclude(i => i.Product)
            .Include(w => w.Items).ThenInclude(i => i.ProductStock)
            .Include(w => w.Store)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<ProductStock?> GetStockByIdAsync(Guid stockId, CancellationToken ct = default) =>
        _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == stockId, ct);

    public Task<List<ProductStock>> GetFefoOrderedAsync(Guid productId, Guid storeId, CancellationToken ct = default) =>
        _db.ProductStocks
            .Where(s => s.ProductId == productId && s.StoreId == storeId && s.Quantity > 0
                     && s.Status != "sold_out" && s.Status != "archived")
            .OrderBy(s => s.ExpiryDate)
            .ToListAsync(ct);

    public async Task AddAsync(WriteOff writeOff, CancellationToken ct = default) =>
        await _db.WriteOffs.AddAsync(writeOff, ct);

    public async Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) =>
        await _db.StockMovements.AddAsync(movement, ct);

    public void Update(WriteOff writeOff) => _db.WriteOffs.Update(writeOff);

    public void UpdateStock(ProductStock stock) => _db.ProductStocks.Update(stock);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
