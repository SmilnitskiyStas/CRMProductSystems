using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Analytics;
using ShelfGuard.Application.Features.Analytics.Dtos;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class AnalyticsRepository : IAnalyticsRepository
{
    private readonly AppDbContext _db;

    public AnalyticsRepository(AppDbContext db) => _db = db;

    public async Task<ExpirySummaryDto> GetExpirySummaryAsync(
        Guid? tenantId, Guid? storeId, bool network, CancellationToken ct = default)
    {
        var query = _db.ProductStocks
            .Where(s => s.Quantity > 0 && s.Status != "sold_out" && s.Status != "archived");

        if (tenantId.HasValue)
            query = query.Where(s => s.TenantId == tenantId.Value);

        if (storeId.HasValue && !network)
            query = query.Where(s => s.StoreId == storeId.Value);

        var batches = await query
            .Select(s => new { s.Status, s.StoreId })
            .ToListAsync(ct);

        var storeIds = batches.Select(b => b.StoreId).Distinct().ToHashSet();
        var stores = await _db.Stores
            .Where(s => storeIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);

        var storeNameMap = stores.ToDictionary(s => s.Id, s => s.Name);

        var byStore = batches
            .GroupBy(b => b.StoreId)
            .Select(g => new ExpirySummaryStoreDto(
                StoreId:   g.Key,
                StoreName: storeNameMap.GetValueOrDefault(g.Key, "Unknown"),
                Safe:      g.Count(b => b.Status == "safe"),
                Warning:   g.Count(b => b.Status == "warning"),
                Critical:  g.Count(b => b.Status == "critical"),
                Expired:   g.Count(b => b.Status == "expired")
            ))
            .ToList();

        return new ExpirySummaryDto(
            Safe:                batches.Count(b => b.Status == "safe"),
            Warning:             batches.Count(b => b.Status == "warning"),
            Critical:            batches.Count(b => b.Status == "critical"),
            Expired:             batches.Count(b => b.Status == "expired"),
            NeedsVerification:   batches.Count(b => b.Status == "needs_verification"),
            Total:               batches.Count,
            Stores:              byStore
        );
    }

    public async Task<WriteOffAnalyticsDto> GetWriteOffAnalyticsAsync(
        Guid? tenantId, Guid? storeId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var query = _db.WriteOffs.Where(w => w.Status == "approved");

        if (tenantId.HasValue)
            query = query.Where(w => w.TenantId == tenantId.Value);

        if (storeId.HasValue)
            query = query.Where(w => w.StoreId == storeId.Value);
        if (from.HasValue)
            query = query.Where(w => w.CreatedAt >= from.Value.ToDateTime(TimeOnly.MinValue));
        if (to.HasValue)
            query = query.Where(w => w.CreatedAt <= to.Value.ToDateTime(TimeOnly.MaxValue));

        var writeOffs = await query
            .Select(w => new { w.Id, w.Reason, w.TotalLossAmount, w.CreatedAt })
            .ToListAsync(ct);

        var byReason = writeOffs
            .GroupBy(w => w.Reason ?? "other")
            .Select(g => new WriteOffByReasonDto(
                Reason:    g.Key,
                Count:     g.Count(),
                TotalLoss: g.Sum(w => w.TotalLossAmount ?? 0m)
            ))
            .OrderByDescending(r => r.TotalLoss)
            .ToList();

        var byDate = writeOffs
            .GroupBy(w => DateOnly.FromDateTime(w.CreatedAt.Date))
            .Select(g => new WriteOffByDateDto(
                Date:      g.Key,
                Count:     g.Count(),
                TotalLoss: g.Sum(w => w.TotalLossAmount ?? 0m)
            ))
            .OrderBy(d => d.Date)
            .ToList();

        return new WriteOffAnalyticsDto(
            TotalDocuments: writeOffs.Count,
            TotalLoss:      writeOffs.Sum(w => w.TotalLossAmount ?? 0m),
            ByReason:       byReason,
            ByDate:         byDate
        );
    }

    public async Task<MovementAnalyticsDto> GetMovementAnalyticsAsync(
        Guid? tenantId, Guid? storeId, string? type, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var query = _db.StockMovements.AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(m => m.TenantId == tenantId.Value);

        if (storeId.HasValue)
            query = query.Where(m => m.FromStoreId == storeId.Value || m.ToStoreId == storeId.Value);
        if (!string.IsNullOrEmpty(type))
            query = query.Where(m => m.MovementType == type);
        if (from.HasValue)
            query = query.Where(m => m.CreatedAt >= from.Value.ToDateTime(TimeOnly.MinValue));
        if (to.HasValue)
            query = query.Where(m => m.CreatedAt <= to.Value.ToDateTime(TimeOnly.MaxValue));

        var movements = await query
            .Select(m => new { m.MovementType, m.Quantity })
            .ToListAsync(ct);

        var byType = movements
            .GroupBy(m => m.MovementType)
            .Select(g => new MovementByTypeDto(
                MovementType:  g.Key,
                Count:         g.Count(),
                TotalQuantity: g.Sum(m => m.Quantity)
            ))
            .OrderByDescending(t => t.Count)
            .ToList();

        return new MovementAnalyticsDto(
            TotalMovements: movements.Count,
            TotalQuantity:  movements.Sum(m => m.Quantity),
            ByType:         byType
        );
    }

    public async Task<IReadOnlyList<ZoneAnalyticsDto>> GetByZoneAsync(
        Guid? tenantId, Guid? storeId, CancellationToken ct = default)
    {
        var query = _db.ProductStocks
            .Where(s => s.ZoneId.HasValue && s.Quantity > 0
                        && s.Status != "sold_out" && s.Status != "archived");

        if (tenantId.HasValue)
            query = query.Where(s => s.TenantId == tenantId.Value);

        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId.Value);

        var batches = await query
            .Select(s => new { s.ZoneId, s.StoreId, s.Status })
            .ToListAsync(ct);

        var zoneIds = batches.Select(b => b.ZoneId!.Value).Distinct().ToHashSet();
        var storeIds = batches.Select(b => b.StoreId).Distinct().ToHashSet();

        var zones = await _db.StoreZones
            .Where(z => zoneIds.Contains(z.Id))
            .Select(z => new { z.Id, z.Name, z.Type, z.StoreId })
            .ToListAsync(ct);

        var storeNames = await _db.Stores
            .Where(s => storeIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var zoneMap = zones.ToDictionary(z => z.Id);

        return batches
            .GroupBy(b => b.ZoneId!.Value)
            .Where(g => zoneMap.ContainsKey(g.Key))
            .Select(g =>
            {
                var zone = zoneMap[g.Key];
                return new ZoneAnalyticsDto(
                    ZoneId:      g.Key,
                    ZoneName:    zone.Name,
                    ZoneType:    zone.Type,
                    StoreId:     zone.StoreId,
                    StoreName:   storeNames.GetValueOrDefault(zone.StoreId, "Unknown"),
                    Safe:        g.Count(b => b.Status == "safe"),
                    Warning:     g.Count(b => b.Status == "warning"),
                    Critical:    g.Count(b => b.Status == "critical"),
                    Expired:     g.Count(b => b.Status == "expired"),
                    TotalBatches: g.Count()
                );
            })
            .OrderByDescending(z => z.Critical + z.Expired)
            .ToList();
    }

    public async Task<IReadOnlyList<CategoryAnalyticsDto>> GetByCategoryAsync(
        Guid? tenantId, Guid? storeId, CancellationToken ct = default)
    {
        var stockQuery = _db.ProductStocks
            .Where(s => s.Quantity > 0 && s.Status != "sold_out" && s.Status != "archived");

        if (tenantId.HasValue)
            stockQuery = stockQuery.Where(s => s.TenantId == tenantId.Value);

        if (storeId.HasValue)
            stockQuery = stockQuery.Where(s => s.StoreId == storeId.Value);

        var batches = await stockQuery
            .Select(s => new { s.ProductId, s.Status, s.Quantity })
            .ToListAsync(ct);

        var productIds = batches.Select(b => b.ProductId).Distinct().ToHashSet();
        var products = await _db.CatalogProducts
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.CategoryId })
            .ToListAsync(ct);

        var categoryIds = products
            .Where(p => p.CategoryId.HasValue)
            .Select(p => p.CategoryId!.Value)
            .Distinct()
            .ToHashSet();

        var categories = await _db.Categories
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var productCategoryMap = products.ToDictionary(p => p.Id, p => p.CategoryId);

        return batches
            .GroupBy(b => productCategoryMap.GetValueOrDefault(b.ProductId))
            .Select(g =>
            {
                var categoryId = g.Key;
                var categoryName = categoryId.HasValue
                    ? categories.GetValueOrDefault(categoryId.Value, "Unknown")
                    : "Без категорії";

                return new CategoryAnalyticsDto(
                    CategoryId:     categoryId,
                    CategoryName:   categoryName,
                    Safe:           g.Count(b => b.Status == "safe"),
                    Warning:        g.Count(b => b.Status == "warning"),
                    Critical:       g.Count(b => b.Status == "critical"),
                    Expired:        g.Count(b => b.Status == "expired"),
                    TotalBatches:   g.Count(),
                    TotalQuantity:  g.Sum(b => b.Quantity)
                );
            })
            .OrderByDescending(c => c.Critical + c.Expired)
            .ToList();
    }

    public async Task<LossesDto> GetLossesAsync(
        Guid? tenantId, Guid? storeId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var query = _db.WriteOffs.Where(w => w.Status == "approved");

        if (tenantId.HasValue)
            query = query.Where(w => w.TenantId == tenantId.Value);

        if (storeId.HasValue)
            query = query.Where(w => w.StoreId == storeId.Value);
        if (from.HasValue)
            query = query.Where(w => w.CreatedAt >= from.Value.ToDateTime(TimeOnly.MinValue));
        if (to.HasValue)
            query = query.Where(w => w.CreatedAt <= to.Value.ToDateTime(TimeOnly.MaxValue));

        var writeOffs = await query
            .Select(w => new { w.StoreId, w.TotalLossAmount })
            .ToListAsync(ct);

        var storeIds = writeOffs.Select(w => w.StoreId).Distinct().ToHashSet();
        var storeNames = await _db.Stores
            .Where(s => storeIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var byStore = writeOffs
            .GroupBy(w => w.StoreId)
            .Select(g => new LossByStoreDto(
                StoreId:      g.Key,
                StoreName:    storeNames.GetValueOrDefault(g.Key, "Unknown"),
                TotalLoss:    g.Sum(w => w.TotalLossAmount ?? 0m),
                WriteOffCount: g.Count()
            ))
            .OrderByDescending(s => s.TotalLoss)
            .ToList();

        var totalLoss = writeOffs.Sum(w => w.TotalLossAmount ?? 0m);
        var count = writeOffs.Count;

        return new LossesDto(
            TotalLoss:             totalLoss,
            TotalWriteOffs:        count,
            AverageLossPerWriteOff: count > 0 ? totalLoss / count : 0m,
            ByStore:               byStore
        );
    }
}
