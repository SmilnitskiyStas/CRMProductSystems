using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class StockRepository : IStockRepository
{
    private readonly AppDbContext _db;

    public StockRepository(AppDbContext db) => _db = db;

    public async Task<List<ProductStock>> GetAllAsync(
        Guid? storeId, string? status, Guid? zoneId, Guid? productId,
        CancellationToken ct = default)
    {
        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId);

        query = ApplyStatusFilter(query, status);

        if (zoneId.HasValue)
            query = query.Where(s => s.ZoneId == zoneId);

        if (productId.HasValue)
            query = query.Where(s => s.ProductId == productId);

        return await query
            .OrderBy(s => s.ExpiryDate)
            .ThenBy(s => s.Product!.Name)
            .ToListAsync(ct);
    }

    public async Task<(List<ProductStock> Items, int Total)> GetPagedAsync(
        Guid[]? storeIds, string? status, Guid? zoneId, Guid? productId,
        int page, int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .AsQueryable();

        if (storeIds is { Length: > 0 })
            query = query.Where(s => storeIds.Contains(s.StoreId));
        query = ApplyStatusFilter(query, status);
        if (zoneId.HasValue)
            query = query.Where(s => s.ZoneId == zoneId);
        if (productId.HasValue)
            query = query.Where(s => s.ProductId == productId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(s => s.ExpiryDate)
            .ThenBy(s => s.Product!.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<ProductStock?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<List<ProductStock>> GetExpiringAsync(Guid? storeId, int days, CancellationToken ct = default)
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(days));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .Where(s => s.Quantity > 0 && s.ExpiryDate > today && s.ExpiryDate <= cutoff);

        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId);

        return await query.OrderBy(s => s.ExpiryDate).ToListAsync(ct);
    }

    public async Task<List<ProductStock>> GetExpiredAsync(Guid? storeId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .Where(s => s.Quantity > 0 && s.ExpiryDate <= today);

        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId);

        return await query.OrderBy(s => s.ExpiryDate).ToListAsync(ct);
    }

    public async Task<List<ProductStock>> GetNeedsCheckAsync(Guid? storeId, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);

        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .Where(s => s.Quantity > 0 && s.LastCheckedAt < cutoff);

        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId);

        return await query.OrderBy(s => s.LastCheckedAt).ToListAsync(ct);
    }

    public Task<List<ProductStock>> GetFefoOrderedAsync(Guid productId, Guid storeId, CancellationToken ct = default) =>
        _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .Where(s => s.ProductId == productId && s.StoreId == storeId && s.Quantity > 0
                     && s.Status != "sold_out" && s.Status != "archived")
            .OrderBy(s => s.ExpiryDate)
            .ToListAsync(ct);

    public async Task<List<ProductStock>> GetActionRequiredAsync(Guid? storeId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var warningCutoff = today.AddDays(14);

        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .Where(s => s.Quantity > 0 && s.ExpiryDate <= warningCutoff);

        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId);

        return await query.OrderBy(s => s.ExpiryDate).ToListAsync(ct);
    }

    public Task<List<ProductStock>> GetDeficitStocksAsync(Guid productId, Guid excludeStoreId, CancellationToken ct = default) =>
        _db.ProductStocks
            .Include(s => s.Store)
            .Include(s => s.Product)
            .Where(s => s.ProductId == productId
                && s.StoreId != excludeStoreId
                && s.Quantity > 0
                && s.Product != null
                && s.Quantity < s.Product.MinStock)
            .ToListAsync(ct);

    public async Task<Dictionary<Guid, List<ProductStock>>> GetDeficitStocksBulkAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default)
    {
        // One query for all products: load every deficit batch across all stores.
        // Ordered by ExpiryDate so a caller taking the first (store-filtered) entry
        // gets the FEFO-nearest deficit — mirrors GetDeficitStocksAsync's per-call ordering.
        var rows = await _db.ProductStocks
            .Include(s => s.Store)
            .Include(s => s.Product)
            .Where(s => productIds.Contains(s.ProductId)
                && s.Quantity > 0
                && s.Product != null
                && s.Quantity < s.Product.MinStock)
            .OrderBy(s => s.ExpiryDate)
            .ToListAsync(ct);

        // Build a lookup: productId → all matching rows (ExpiryDate ascending).
        var lookup = rows.GroupBy(s => s.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Ensure every requested productId has an entry so callers can do lookup[id].
        foreach (var id in productIds)
            lookup.TryAdd(id, []);

        return lookup;
    }

    public Task<List<Location>> GetProductionStoresAsync(CancellationToken ct = default) =>
        _db.Locations
            .Where(s => s.IsActive && (s.Type == "production" || s.Type == "distribution"))
            .ToListAsync(ct);

    public async Task<Dictionary<string, int>> GetStatusCountsAsync(Guid[]? storeIds, CancellationToken ct = default)
    {
        var baseQuery = _db.ProductStocks.Where(s => s.Quantity > 0).AsQueryable();
        if (storeIds is { Length: > 0 })
            baseQuery = baseQuery.Where(s => storeIds.Contains(s.StoreId));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var criticalCutoff = today.AddDays(StockStatus.CriticalDays);
        var warningCutoff = today.AddDays(StockStatus.WarningDays);
        var verificationCutoff = DateTime.UtcNow.AddDays(-StockStatus.NeedsVerificationDays);

        // Single round-trip: fetch only the two date columns, count in memory.
        var rows = await baseQuery
            .Select(s => new { s.ExpiryDate, s.LastCheckedAt })
            .ToListAsync(ct);

        return new Dictionary<string, int>
        {
            ["expired"] = rows.Count(s => s.ExpiryDate <= today),
            ["critical"] = rows.Count(s => s.ExpiryDate > today && s.ExpiryDate <= criticalCutoff),
            ["warning"] = rows.Count(s => s.ExpiryDate > criticalCutoff && s.ExpiryDate <= warningCutoff),
            ["needs_verification"] = rows.Count(s => s.ExpiryDate > warningCutoff && s.LastCheckedAt <= verificationCutoff),
            ["safe"] = rows.Count(s => s.ExpiryDate > warningCutoff && s.LastCheckedAt > verificationCutoff),
        };
    }

    public async Task<List<(Guid? ZoneId, string ZoneName, string ZoneType, string Status)>> GetStockByZoneRawAsync(
        Guid[]? storeIds, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var criticalCutoff = today.AddDays(StockStatus.CriticalDays);
        var warningCutoff = today.AddDays(StockStatus.WarningDays);
        var verificationCutoff = DateTime.UtcNow.AddDays(-StockStatus.NeedsVerificationDays);

        var query = _db.ProductStocks
            .Where(s => s.Quantity > 0)
            .Include(s => s.Zone)
            .AsQueryable();

        if (storeIds is { Length: > 0 })
            query = query.Where(s => storeIds.Contains(s.StoreId));

        var rows = await query
            .Select(s => new
            {
                s.ZoneId,
                ZoneName = s.Zone != null ? s.Zone.Name : "Без зони",
                ZoneType = s.Zone != null ? s.Zone.Type : "other",
                s.ExpiryDate,
                s.LastCheckedAt,
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var status = r.ExpiryDate <= today ? "expired"
                : r.ExpiryDate <= criticalCutoff ? "critical"
                : r.ExpiryDate <= warningCutoff ? "warning"
                : r.LastCheckedAt <= verificationCutoff ? "needs_verification"
                : "safe";
            return (r.ZoneId, r.ZoneName, r.ZoneType, status);
        }).ToList();
    }

    public async Task AddAsync(ProductStock stock, CancellationToken ct = default) =>
        await _db.ProductStocks.AddAsync(stock, ct);

    public async Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) =>
        await _db.StockMovements.AddAsync(movement, ct);

    public void Update(ProductStock stock) =>
        _db.ProductStocks.Update(stock);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Filters by the *computed* (real-time) status rather than the stored column,
    /// matching the logic in StockStatus.Compute so the filter is always accurate.
    /// </summary>
    private static IQueryable<ProductStock> ApplyStatusFilter(
        IQueryable<ProductStock> query, string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return query;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var criticalCutoff = today.AddDays(StockStatus.CriticalDays);
        var warningCutoff = today.AddDays(StockStatus.WarningDays);
        var verificationCutoff = DateTime.UtcNow.AddDays(-StockStatus.NeedsVerificationDays);

        return status switch
        {
            "sold_out" => query.Where(s => s.Quantity <= 0),
            "expired" => query.Where(s => s.Quantity > 0 && s.ExpiryDate <= today),
            "critical" => query.Where(s => s.Quantity > 0 && s.ExpiryDate > today && s.ExpiryDate <= criticalCutoff),
            "warning" => query.Where(s => s.Quantity > 0 && s.ExpiryDate > criticalCutoff && s.ExpiryDate <= warningCutoff),
            "safe" => query.Where(s => s.Quantity > 0 && s.ExpiryDate > warningCutoff && s.LastCheckedAt > verificationCutoff),
            "needs_verification" => query.Where(s => s.Quantity > 0 && s.ExpiryDate > warningCutoff && s.LastCheckedAt <= verificationCutoff),
            _ => query.Where(_ => false)
        };
    }
}
