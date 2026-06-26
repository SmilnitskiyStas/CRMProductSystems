using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Analytics;
using ShelfGuard.Application.Features.Analytics.Dtos;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class AnalyticsRepository : IAnalyticsRepository
{
    private readonly AppDbContext _db;

    public AnalyticsRepository(AppDbContext db) => _db = db;

    public async Task<ExpirySummaryDto> GetExpirySummaryAsync(
        Guid? tenantId, Guid? storeId, bool network, CancellationToken ct = default)
    {
        var query = _db.ProductStocks
            .Where(s => s.Quantity > 0);

        if (tenantId.HasValue)
            query = query.Where(s => s.TenantId == tenantId.Value);

        if (storeId.HasValue && !network)
            query = query.Where(s => s.StoreId == storeId.Value);

        var rows = await query
            .Select(s => new { s.ExpiryDate, s.LastCheckedAt, s.StoreId })
            .ToListAsync(ct);

        var thresholds = StatusThresholds.Now();
        var batches = rows.Select(r => new
        {
            r.StoreId,
            Status = ComputeStatus(r.ExpiryDate, r.LastCheckedAt, thresholds)
        }).ToList();

        var storeIds = batches.Select(b => b.StoreId).Distinct().ToHashSet();
        var stores = await _db.Locations
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

        // Single query: join zone and store via navigation properties
        var rows = await query
            .Select(s => new
            {
                s.ZoneId,
                ZoneName  = s.Zone!.Name,
                ZoneType  = s.Zone!.Type,
                StoreId   = s.Zone!.LocationId,
                StoreName = s.Zone!.Location!.Name,
                s.ExpiryDate,
                s.LastCheckedAt,
            })
            .ToListAsync(ct);

        var thresholds = StatusThresholds.Now();
        var batches = rows.Select(r => new
        {
            r.ZoneId,
            r.ZoneName,
            r.ZoneType,
            r.StoreId,
            r.StoreName,
            Status = ComputeStatus(r.ExpiryDate, r.LastCheckedAt, thresholds)
        }).ToList();

        return batches
            .GroupBy(b => b.ZoneId!.Value)
            .Select(g =>
            {
                var first = g.First();
                return new ZoneAnalyticsDto(
                    ZoneId:       g.Key,
                    ZoneName:     first.ZoneName,
                    ZoneType:     first.ZoneType,
                    StoreId:      first.StoreId,
                    StoreName:    first.StoreName,
                    Safe:         g.Count(b => b.Status == "safe"),
                    Warning:      g.Count(b => b.Status == "warning"),
                    Critical:     g.Count(b => b.Status == "critical"),
                    Expired:      g.Count(b => b.Status == "expired"),
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

        // Single query: join product and category via navigation properties
        var rows = await stockQuery
            .Select(s => new
            {
                CategoryId   = s.Product!.CategoryId,
                CategoryName = s.Product!.Category != null ? s.Product.Category.Name : null,
                s.ExpiryDate,
                s.LastCheckedAt,
                s.Quantity
            })
            .ToListAsync(ct);

        var thresholds = StatusThresholds.Now();
        var batches = rows.Select(r => new
        {
            r.CategoryId,
            r.CategoryName,
            r.Quantity,
            Status = ComputeStatus(r.ExpiryDate, r.LastCheckedAt, thresholds)
        }).ToList();

        return batches
            .GroupBy(b => b.CategoryId)
            .Select(g =>
            {
                var categoryId   = g.Key;
                var categoryName = categoryId.HasValue
                    ? (g.First().CategoryName ?? "Unknown")
                    : "Без категорії";

                return new CategoryAnalyticsDto(
                    CategoryId:    categoryId,
                    CategoryName:  categoryName,
                    Safe:          g.Count(b => b.Status == "safe"),
                    Warning:       g.Count(b => b.Status == "warning"),
                    Critical:      g.Count(b => b.Status == "critical"),
                    Expired:       g.Count(b => b.Status == "expired"),
                    TotalBatches:  g.Count(),
                    TotalQuantity: g.Sum(b => b.Quantity)
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

        // Single query: join store name via navigation property
        var writeOffs = await query
            .Select(w => new
            {
                w.StoreId,
                StoreName      = w.Store != null ? w.Store.Name : null,
                w.TotalLossAmount
            })
            .ToListAsync(ct);

        var byStore = writeOffs
            .GroupBy(w => w.StoreId)
            .Select(g => new LossByStoreDto(
                StoreId:       g.Key,
                StoreName:     g.First().StoreName ?? "Unknown",
                TotalLoss:     g.Sum(w => w.TotalLossAmount ?? 0m),
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

    // ── POS analytics ─────────────────────────────────────────────────────

    public async Task<PosAnalyticsSummaryDto> GetPosSummaryAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue);

        var txQuery = BuildPosTransactionQuery(tenantId, storeId, fromDt, toDt);

        var txData = await txQuery
            .Select(t => new { t.TotalAmount, t.PaymentType })
            .ToListAsync(ct);

        var shiftQuery = _db.PosShifts.AsQueryable();
        if (tenantId.HasValue)
            shiftQuery = shiftQuery.Where(s => s.TenantId == tenantId.Value);
        if (storeId.HasValue)
            shiftQuery = shiftQuery.Where(s => s.StoreId == storeId.Value);
        shiftQuery = shiftQuery.Where(s => s.OpenedAt >= fromDt && s.OpenedAt <= toDt);

        var shiftCount = await shiftQuery.CountAsync(ct);

        var totalRevenue = txData.Sum(t => t.TotalAmount);
        var count        = txData.Count;

        return new PosAnalyticsSummaryDto(
            TotalRevenue:     totalRevenue,
            TransactionCount: count,
            AverageTicket:    count > 0 ? totalRevenue / count : 0m,
            CashRevenue:      txData.Where(t => t.PaymentType.Equals("Cash", StringComparison.OrdinalIgnoreCase))
                                    .Sum(t => t.TotalAmount),
            CardRevenue:      txData.Where(t => t.PaymentType.Equals("Card", StringComparison.OrdinalIgnoreCase))
                                    .Sum(t => t.TotalAmount),
            ShiftCount:       shiftCount,
            From:             from,
            To:               to);
    }

    public async Task<PosRevenueTrendDto> GetPosRevenueTrendAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, string groupBy, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue);

        var txData = await BuildPosTransactionQuery(tenantId, storeId, fromDt, toDt)
            .Select(t => new { t.TotalAmount, t.CreatedAt })
            .ToListAsync(ct);

        List<RevenueTrendPointDto> points;

        if (groupBy == "week")
        {
            points = txData
                .GroupBy(t => IsoWeekStart(t.CreatedAt))
                .OrderBy(g => g.Key)
                .Select(g => new RevenueTrendPointDto(
                    Date:         DateOnly.FromDateTime(g.Key),
                    Revenue:      g.Sum(t => t.TotalAmount),
                    Transactions: g.Count()))
                .ToList();
        }
        else
        {
            points = txData
                .GroupBy(t => DateOnly.FromDateTime(t.CreatedAt.Date))
                .OrderBy(g => g.Key)
                .Select(g => new RevenueTrendPointDto(
                    Date:         g.Key,
                    Revenue:      g.Sum(t => t.TotalAmount),
                    Transactions: g.Count()))
                .ToList();
        }

        return new PosRevenueTrendDto(Points: points, GroupBy: groupBy == "week" ? "week" : "day");
    }

    public async Task<PosTopProductsDto> GetPosTopProductsAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, int limit, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue);

        // Use IQueryable subquery instead of materialising txIds into memory
        var txQuery = BuildPosTransactionQuery(tenantId, storeId, fromDt, toDt);

        var items = await _db.PosTransactionItems
            .Where(i => txQuery.Select(t => t.Id).Contains(i.TransactionId))
            .Select(i => new
            {
                i.ProductId,
                ProductName = i.Product!.Name,
                Barcode     = i.Product!.Barcodes.Count > 0 ? i.Product.Barcodes[0] : null,
                i.PriceFinal,
                i.Quantity,
                i.TransactionId
            })
            .ToListAsync(ct);

        var topItems = items
            .GroupBy(i => i.ProductId)
            .Select(g => new TopProductDto(
                ProductId:        g.Key,
                ProductName:      g.First().ProductName,
                Barcode:          g.First().Barcode ?? string.Empty,
                TotalRevenue:     g.Sum(i => i.PriceFinal * i.Quantity),
                TotalQuantity:    g.Sum(i => i.Quantity),
                TransactionCount: g.Select(i => i.TransactionId).Distinct().Count()))
            .OrderByDescending(p => p.TotalRevenue)
            .Take(limit)
            .ToList();

        return new PosTopProductsDto(Items: topItems);
    }

    public async Task<PosCashierStatsDto> GetPosCashierStatsAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue);

        var txData = await BuildPosTransactionQuery(tenantId, storeId, fromDt, toDt)
            .Where(t => t.CashierId.HasValue)
            .Join(_db.Users,
                  t => t.CashierId!.Value,
                  u => u.Id,
                  (t, u) => new { t.CashierId, u.FullName, t.TotalAmount, t.ShiftId })
            .ToListAsync(ct);

        var cashiers = txData
            .GroupBy(t => t.CashierId!.Value)
            .Select(g =>
            {
                var revenue = g.Sum(t => t.TotalAmount);
                var count   = g.Count();
                return new CashierStatDto(
                    CashierId:        g.Key,
                    CashierName:      g.First().FullName,
                    TotalRevenue:     revenue,
                    TransactionCount: count,
                    AverageTicket:    count > 0 ? revenue / count : 0m,
                    ShiftCount:       g.Where(t => t.ShiftId.HasValue)
                                       .Select(t => t.ShiftId!.Value)
                                       .Distinct()
                                       .Count());
            })
            .OrderByDescending(c => c.TotalRevenue)
            .ToList();

        return new PosCashierStatsDto(Cashiers: cashiers);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private IQueryable<Domain.Entities.PosTransaction> BuildPosTransactionQuery(
        Guid? tenantId, Guid? storeId, DateTime fromDt, DateTime toDt)
    {
        var query = _db.PosTransactions
            .Where(t => t.Status != "fiscalization_failed"
                     && t.CreatedAt >= fromDt
                     && t.CreatedAt <= toDt);

        if (tenantId.HasValue)
            query = query.Where(t => t.TenantId == tenantId.Value);

        if (storeId.HasValue)
            query = query.Where(t => t.StoreId == storeId.Value);

        return query;
    }

    private static DateTime IsoWeekStart(DateTime dt)
    {
        var dow = (int)dt.DayOfWeek;
        // ISO week: Monday = 1, Sunday = 7
        var offset = dow == 0 ? -6 : 1 - dow;
        return dt.Date.AddDays(offset);
    }

    // Mirrors StockStatus.Compute so analytics and stock counts are always in sync.
    private static string ComputeStatus(DateOnly expiryDate, DateTime lastCheckedAt, StatusThresholds t)
    {
        if (expiryDate <= t.Today)                      return "expired";
        if (expiryDate <= t.CriticalCutoff)             return "critical";
        if (expiryDate <= t.WarningCutoff)              return "warning";
        if (lastCheckedAt <= t.VerificationCutoff)      return "needs_verification";
        return "safe";
    }

    private sealed record StatusThresholds(
        DateOnly Today,
        DateOnly CriticalCutoff,
        DateOnly WarningCutoff,
        DateTime VerificationCutoff)
    {
        public static StatusThresholds Now()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return new StatusThresholds(
                Today:               today,
                CriticalCutoff:      today.AddDays(StockStatus.CriticalDays),
                WarningCutoff:       today.AddDays(StockStatus.WarningDays),
                VerificationCutoff:  DateTime.UtcNow.AddDays(-StockStatus.NeedsVerificationDays)
            );
        }
    }
}
