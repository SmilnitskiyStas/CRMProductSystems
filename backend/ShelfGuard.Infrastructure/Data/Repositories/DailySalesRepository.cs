using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class DailySalesRepository : IDailySalesRepository
{
    private readonly AppDbContext _db;

    /// <summary>Ukrainian-collation ordering only where the provider supports it (Npgsql).</summary>
    private bool Uk => _db.Database.IsNpgsql();

    public DailySalesRepository(AppDbContext db) => _db = db;

    /// <summary>Ukrainian alphabet order, Cyrillic before Latin (migration AddUkrainianNameCollation).</summary>
    private const string NameCollation = "uk_cyrl_first";

    public async Task<(List<DailySale> Items, int TotalCount)> GetPagedAsync(
        Guid[]? storeIds, Guid? productId, DateOnly? from, DateOnly? to,
        int page, int pageSize, string? sortBy = null, bool sortDescending = true,
        CancellationToken ct = default)
    {
        var query = _db.DailySales.AsQueryable();

        if (storeIds is { Length: > 0 }) query = query.Where(s => storeIds.Contains(s.StoreId));
        if (productId.HasValue) query = query.Where(s => s.ProductId == productId);
        if (from.HasValue) query = query.Where(s => s.Date >= from);
        if (to.HasValue) query = query.Where(s => s.Date <= to);

        var total = await query.CountAsync(ct);

        // Id as the last key keeps the page boundaries stable when date + product name tie.
        var ordered = (sortBy?.ToLowerInvariant(), sortDescending) switch
        {
            ("product", false) => query.OrderByUk(s => s.Product!.Name, Uk),
            ("product", true) => query.OrderByDescendingUk(s => s.Product!.Name, Uk),
            ("store", false) => query.OrderByUk(s => s.Store!.Name, Uk),
            ("store", true) => query.OrderByDescendingUk(s => s.Store!.Name, Uk),
            ("sold", false) => query.OrderBy(s => s.QuantitySold),
            ("sold", true) => query.OrderByDescending(s => s.QuantitySold),
            ("eod", false) => query.OrderBy(s => s.QuantityEndOfDay),
            ("eod", true) => query.OrderByDescending(s => s.QuantityEndOfDay),
            ("source", false) => query.OrderBy(s => s.Source),
            ("source", true) => query.OrderByDescending(s => s.Source),
            (_, false) => query.OrderBy(s => s.Date),
            _ => query.OrderByDescending(s => s.Date),
        };

        var items = await ordered
            .ThenByDescending(s => s.Date)
            .ThenByUk(s => s.Product!.Name, Uk)
            .ThenBy(s => s.Id)
            .Include(s => s.Product)
            .Include(s => s.Store)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<DailySale?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.DailySales
            .Include(s => s.Product)
            .Include(s => s.Store)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<DailySale?> FindAsync(Guid storeId, Guid productId, DateOnly date, CancellationToken ct = default) =>
        _db.DailySales.FirstOrDefaultAsync(
            s => s.StoreId == storeId && s.ProductId == productId && s.Date == date, ct);

    public async Task<Dictionary<string, Guid>> GetProductIdsByBarcodesAsync(
        IReadOnlyCollection<string> barcodes, CancellationToken ct = default)
    {
        // Materialise to client side: JSONB array intersection can't be expressed
        // as a single EF/SQL predicate when filtering against an in-memory list.
        // Items table is small; this is acceptable.
        return await _db.Items
            .Where(p => p.Barcodes.Any(b => barcodes.Contains(b)))
            .Select(p => new { p.Id, p.Barcodes })
            .ToListAsync(ct)
            .ContinueWith(t => t.Result
                .SelectMany(p => p.Barcodes
                    .Where(b => barcodes.Contains(b))
                    .Select(b => new { Barcode = b, p.Id }))
                .GroupBy(x => x.Barcode)
                .ToDictionary(g => g.Key, g => g.First().Id));
    }

    public Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default) =>
        _db.Locations.AnyAsync(s => s.Id == storeId && s.IsActive, ct);

    public Task<bool> ProductExistsAsync(Guid productId, CancellationToken ct = default) =>
        _db.Items.AnyAsync(p => p.Id == productId && p.IsActive, ct);

    public async Task AddAsync(DailySale sale, CancellationToken ct = default) =>
        await _db.DailySales.AddAsync(sale, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
