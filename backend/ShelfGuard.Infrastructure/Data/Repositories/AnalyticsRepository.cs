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
            query = query.Where(w => w.CreatedAt >= ToUtcStart(from.Value));
        if (to.HasValue)
            query = query.Where(w => w.CreatedAt <= ToUtcEnd(to.Value));

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
            query = query.Where(m => m.CreatedAt >= ToUtcStart(from.Value));
        if (to.HasValue)
            query = query.Where(m => m.CreatedAt <= ToUtcEnd(to.Value));

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
            query = query.Where(w => w.CreatedAt >= ToUtcStart(from.Value));
        if (to.HasValue)
            query = query.Where(w => w.CreatedAt <= ToUtcEnd(to.Value));

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

    // ── TASK-481: category/losses product drill-down ────────────────────────

    public async Task<CategoryProductBreakdownDto> GetCategoryProductBreakdownAsync(
        Guid? tenantId, Guid? storeId, Guid? categoryId, DateOnly from, DateOnly to, bool includeMargin, CancellationToken ct = default)
    {
        // ── Stock side: same rollup as GetByCategoryAsync, scoped to one category and grouped
        // by product instead of by category. ────────────────────────────────────────────────
        var stockQuery = _db.ProductStocks
            .Where(s => s.Quantity > 0 && s.Status != "sold_out" && s.Status != "archived");

        if (tenantId.HasValue)
            stockQuery = stockQuery.Where(s => s.TenantId == tenantId.Value);
        if (storeId.HasValue)
            stockQuery = stockQuery.Where(s => s.StoreId == storeId.Value);

        // null category_id means the "uncategorized" bucket (mirrors GetByCategoryAsync's own
        // null-key group), not "all categories".
        stockQuery = categoryId.HasValue
            ? stockQuery.Where(s => s.Product!.CategoryId == categoryId.Value)
            : stockQuery.Where(s => s.Product!.CategoryId == null);

        var stockRows = await stockQuery
            .Select(s => new { s.ProductId, ProductName = s.Product!.Name, s.ExpiryDate, s.LastCheckedAt, s.Quantity })
            .ToListAsync(ct);

        var thresholds = StatusThresholds.Now();
        var stockByProduct = stockRows
            .Select(r => new
            {
                r.ProductId,
                r.ProductName,
                r.Quantity,
                Status = ComputeStatus(r.ExpiryDate, r.LastCheckedAt, thresholds)
            })
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => new
            {
                ProductName   = g.First().ProductName,
                Safe          = g.Count(b => b.Status == "safe"),
                Warning       = g.Count(b => b.Status == "warning"),
                Critical      = g.Count(b => b.Status == "critical"),
                Expired       = g.Count(b => b.Status == "expired"),
                TotalQuantity = g.Sum(b => b.Quantity)
            });

        // ── Sales side: same rollup as GetPosTopProductsAsync, scoped to one category. ───────
        var fromDt = ToUtcStart(from);
        var toDt   = ToUtcEnd(to);
        var txQuery = BuildPosTransactionQuery(tenantId, storeId, fromDt, toDt);

        var salesItemsQuery = _db.PosTransactionItems
            .Where(i => txQuery.Select(t => t.Id).Contains(i.TransactionId));

        salesItemsQuery = categoryId.HasValue
            ? salesItemsQuery.Where(i => i.Product!.CategoryId == categoryId.Value)
            : salesItemsQuery.Where(i => i.Product!.CategoryId == null);

        var salesRows = await salesItemsQuery
            .Select(i => new { i.ProductId, ProductName = i.Product!.Name, i.PriceFinal, i.Quantity })
            .ToListAsync(ct);

        var salesByProduct = salesRows
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => new
            {
                ProductName  = g.First().ProductName,
                SalesRevenue = g.Sum(i => i.PriceFinal * i.Quantity),
                UnitsSold    = g.Sum(i => i.Quantity)
            });

        // ── Category name: same null -> "Без категорії" convention as GetByCategoryAsync. ───
        var categoryName = categoryId.HasValue
            ? await _db.Categories
                .Where(c => c.Id == categoryId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct) ?? "Unknown"
            : "Без категорії";

        // ── Margin (ADR-027): current catalog Item.PricePurchase, retroactive estimate — only
        // looked up when the caller cleared AnalyticsAuthorization.CanViewMargin upstream in the
        // controller. Repository stays authorization-agnostic: it only reacts to the bool.
        var productIds = stockByProduct.Keys.Union(salesByProduct.Keys).ToList();

        var purchasePrices = includeMargin && productIds.Count > 0
            ? await _db.Items
                .Where(i => productIds.Contains(i.Id))
                .Select(i => new { i.Id, i.PricePurchase })
                .ToDictionaryAsync(x => x.Id, x => x.PricePurchase, ct)
            : new Dictionary<Guid, decimal?>();

        // ── ADU (TASK-491): bulk-fetched in one extra query, only when the request is
        // store-scoped -- ProductAdu is per-(product, store), so a network-wide/multi-store
        // rollup has no single meaningful ADU to divide by (DaysOfStockRemaining stays null for
        // every row below in that case). Keyed by ProductId via ToDictionaryAsync -- same
        // bulk-fetch-then-Dictionary-lookup shape as purchasePrices just above, deliberately not
        // one query per product (N+1).
        var aduByProduct = new Dictionary<Guid, decimal?>();
        if (storeId.HasValue && productIds.Count > 0)
        {
            var aduQuery = _db.ProductAdus
                .Where(a => a.StoreId == storeId.Value && productIds.Contains(a.ProductId));
            if (tenantId.HasValue)
                aduQuery = aduQuery.Where(a => a.TenantId == tenantId.Value);

            aduByProduct = await aduQuery
                .Select(a => new { a.ProductId, a.AduEffective })
                .ToDictionaryAsync(x => x.ProductId, x => x.AduEffective, ct);
        }

        var products = productIds
            .Select(productId =>
            {
                var hasStock = stockByProduct.TryGetValue(productId, out var stock);
                var hasSales = salesByProduct.TryGetValue(productId, out var sales);

                var salesRevenue = hasSales ? sales!.SalesRevenue : 0m;
                var unitsSold    = hasSales ? sales!.UnitsSold : 0m;

                // Two distinct "no value" cases, kept separate per the DTO's own doc comment:
                // includeMargin == false -> always null; includeMargin == true but no
                // PricePurchase on file -> also null, never coerced to 0.
                decimal? marginAmount = null;
                decimal? marginPercent = null;
                if (includeMargin && purchasePrices.TryGetValue(productId, out var pricePurchase) && pricePurchase.HasValue)
                {
                    marginAmount  = salesRevenue - unitsSold * pricePurchase.Value;
                    marginPercent = salesRevenue == 0m ? null : Math.Round(marginAmount.Value / salesRevenue * 100m, 2);
                }

                // DaysOfStockRemaining (TASK-491): null unless store-scoped (aduByProduct is empty
                // otherwise) AND a ProductAdu row exists with a nonzero AduEffective -- the same
                // division-by-zero guard doubles as the "no usage history yet" representation,
                // since that's a real, valid state rather than an error.
                decimal? daysOfStockRemaining = null;
                if (aduByProduct.TryGetValue(productId, out var aduEffective) && aduEffective.HasValue && aduEffective.Value != 0m)
                {
                    var totalQuantity = hasStock ? stock!.TotalQuantity : 0m;
                    daysOfStockRemaining = Math.Round(totalQuantity / aduEffective.Value, 1);
                }

                return new CategoryProductRowDto(
                    ProductId:     productId,
                    ProductName:   hasStock ? stock!.ProductName : sales!.ProductName,
                    Safe:          hasStock ? stock!.Safe : 0,
                    Warning:       hasStock ? stock!.Warning : 0,
                    Critical:      hasStock ? stock!.Critical : 0,
                    Expired:       hasStock ? stock!.Expired : 0,
                    TotalQuantity: hasStock ? stock!.TotalQuantity : 0m,
                    SalesRevenue:  salesRevenue,
                    UnitsSold:     unitsSold,
                    MarginAmount:  marginAmount,
                    MarginPercent: marginPercent,
                    DaysOfStockRemaining: daysOfStockRemaining);
            })
            .OrderByDescending(p => p.SalesRevenue)
            .ToList();

        return new CategoryProductBreakdownDto(
            CategoryId:   categoryId,
            CategoryName: categoryName,
            Products:     products);
    }

    public async Task<LossesByProductDto> GetLossesByProductAsync(
        Guid? tenantId, Guid? storeId, string? reason, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var writeOffQuery = _db.WriteOffs
            .Where(w => w.Status == "approved"
                     && w.CreatedAt >= ToUtcStart(from)
                     && w.CreatedAt <= ToUtcEnd(to));

        if (tenantId.HasValue)
            writeOffQuery = writeOffQuery.Where(w => w.TenantId == tenantId.Value);
        if (storeId.HasValue)
            writeOffQuery = writeOffQuery.Where(w => w.StoreId == storeId.Value);

        if (!string.IsNullOrEmpty(reason))
        {
            // "other" mirrors GetWriteOffAnalyticsAsync's own `w.Reason ?? "other"` bucket label:
            // a write-off with no reason recorded is what renders as "other" in that aggregate,
            // so drilling into "other" here must include those rows too, not just a literal
            // Reason == "other" match.
            writeOffQuery = reason == "other"
                ? writeOffQuery.Where(w => w.Reason == null || w.Reason == "other")
                : writeOffQuery.Where(w => w.Reason == reason);
        }

        // Subquery-Contains join, same shape as GetPosTopProductsAsync's item/transaction join.
        var itemRows = await _db.WriteOffItems
            .Where(i => writeOffQuery.Select(w => w.Id).Contains(i.WriteOffId))
            .Select(i => new { i.ProductId, ProductName = i.Product!.Name, i.Quantity, i.LossAmount })
            .ToListAsync(ct);

        var byProduct = itemRows
            .GroupBy(i => i.ProductId)
            .Select(g => new
            {
                ProductId   = g.Key,
                ProductName = g.First().ProductName,
                Quantity    = g.Sum(i => i.Quantity),
                LossAmount  = g.Sum(i => i.LossAmount ?? 0m)
            })
            .ToList();

        var totalLoss = byProduct.Sum(p => p.LossAmount);

        var products = byProduct
            .Select(p => new LossByProductRowDto(
                ProductId:    p.ProductId,
                ProductName:  p.ProductName,
                Quantity:     p.Quantity,
                LossAmount:   p.LossAmount,
                SharePercent: totalLoss == 0m ? 0m : Math.Round(p.LossAmount / totalLoss * 100m, 2)))
            .OrderByDescending(p => p.LossAmount)
            .ToList();

        return new LossesByProductDto(TotalLoss: totalLoss, Products: products);
    }

    // ── TASK-489: losses/write-offs trend over time (mirrors GetPosRevenueTrendAsync's shape,
    // "approved" + date-range filter mirrors GetLossesAsync/GetLossesByProductAsync above) ────

    public async Task<LossesTrendDto> GetLossesTrendAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, string groupBy, CancellationToken ct = default)
    {
        var fromDt = ToUtcStart(from);
        var toDt   = ToUtcEnd(to);

        var query = _db.WriteOffs
            .Where(w => w.Status == "approved"
                     && w.CreatedAt >= fromDt
                     && w.CreatedAt <= toDt);

        if (tenantId.HasValue)
            query = query.Where(w => w.TenantId == tenantId.Value);
        if (storeId.HasValue)
            query = query.Where(w => w.StoreId == storeId.Value);

        var resolvedGroupBy = groupBy == "week" ? "week" : "day";

        // SQL-side GroupBy — deliberately unlike GetLossesAsync/GetWriteOffAnalyticsAsync above
        // (both materialize all matching WriteOffs into memory before grouping — an accepted
        // pattern for those lower-cardinality aggregates, not to be copied here). Mirrors
        // GetProductSalesTrendAsync's (TASK-482) two-step shape instead: aggregate into an
        // anonymous type in SQL, terminate with ToListAsync, then map the already-collapsed
        // (<=366-row) points into the DTO record in a cheap second pass.
        //
        // EF.Functions has no DateTrunc/week helper in this repo's installed Npgsql EF Core
        // provider (see GetProductSalesTrendAsync's comment for the full root-cause). "day"
        // relies on the provider's built-in DateTime.Date translation (-> date_trunc('day',
        // "CreatedAt", 'UTC')). "week" inlines the same Monday-anchored ISO offset arithmetic
        // IsoWeekStart() below uses (dow == 0 ? -6 : 1 - dow) as translatable DateTime member
        // arithmetic instead of calling that method directly (EF cannot translate a call to an
        // arbitrary private C# method) — same pattern already verified translatable and correct
        // against real data in GetProductSalesTrendAsync.
        var grouped = resolvedGroupBy == "week"
            ? await query
                .GroupBy(w => w.CreatedAt.Date.AddDays((int)w.CreatedAt.DayOfWeek == 0 ? -6 : 1 - (int)w.CreatedAt.DayOfWeek))
                .Select(g => new
                {
                    Date = g.Key,
                    TotalLoss = g.Sum(w => w.TotalLossAmount ?? 0m),
                    Count = g.Count()
                })
                .OrderBy(p => p.Date)
                .ToListAsync(ct)
            : await query
                .GroupBy(w => w.CreatedAt.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    TotalLoss = g.Sum(w => w.TotalLossAmount ?? 0m),
                    Count = g.Count()
                })
                .OrderBy(p => p.Date)
                .ToListAsync(ct);

        var points = grouped
            .Select(p => new LossesTrendPointDto(
                Date: DateOnly.FromDateTime(p.Date),
                TotalLoss: p.TotalLoss,
                Count: p.Count))
            .ToList();

        return new LossesTrendDto(Points: points, GroupBy: resolvedGroupBy);
    }

    // ── POS analytics ─────────────────────────────────────────────────────

    public async Task<PosAnalyticsSummaryDto> GetPosSummaryAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromDt = ToUtcStart(from);
        var toDt   = ToUtcEnd(to);

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
        var fromDt = ToUtcStart(from);
        var toDt   = ToUtcEnd(to);

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
        var fromDt = ToUtcStart(from);
        var toDt   = ToUtcEnd(to);

        // Use IQueryable subquery instead of materialising txIds into memory
        var txQuery = BuildPosTransactionQuery(tenantId, storeId, fromDt, toDt);

        var items = await _db.PosTransactionItems
            .Where(i => txQuery.Select(t => t.Id).Contains(i.TransactionId))
            .Select(i => new
            {
                i.ProductId,
                ProductName = i.Product!.Name,
                // jsonb-mapped List<string>: Count/indexer are not SQL-translatable (BUG-008) —
                // select the whole list and take the first element client-side below.
                Barcodes    = i.Product!.Barcodes,
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
                Barcode:          g.First().Barcodes?.FirstOrDefault() ?? string.Empty,
                TotalRevenue:     g.Sum(i => i.PriceFinal * i.Quantity),
                TotalQuantity:    g.Sum(i => i.Quantity),
                TransactionCount: g.Select(i => i.TransactionId).Distinct().Count()))
            .OrderByDescending(p => p.TotalRevenue)
            .Take(limit)
            .ToList();

        return new PosTopProductsDto(Items: topItems);
    }

    // ── TASK-490: worst-performing products / dead stock ─────────────────────
    //
    // Deliberately NOT "GetPosTopProductsAsync sorted ascending" -- that query groups
    // PosTransactionItems, so a product with zero matching rows in the period never appears in a
    // GroupBy over that table at all. "Dead stock" specifically needs those zero-sale products
    // surfaced (they're the more actionable/valuable signal), so this starts from the opposite
    // side: active items that currently have on-hand stock (the catalog/stock rollup shape
    // GetByCategoryAsync above already established, including its Status != sold_out/archived
    // exclusion -- a batch in either of those statuses isn't meaningfully "on the shelf" even if
    // its Quantity column hasn't been zeroed out), then LEFT-JOINs a sales rollup for the period
    // (same aggregate shape as GetPosTopProductsAsync above), COALESCEing missing sales to 0.
    //
    // Two-query shape rather than one LINQ query with a LEFT JOIN + GroupBy: this repo's EF
    // Core/Npgsql combination has already needed several workarounds in this file for translating
    // GroupBy combined with anything beyond plain scalar aggregates (see GetProductSalesTrendAsync
    // and GetLossesTrendAsync's comments), and a LEFT JOIN into a second GroupBy stacks that risk
    // further. Splitting into (1) a SQL-side stock aggregate, scalar-only just like those two
    // methods, and (2) a sales aggregate pre-filtered down to just that stock aggregate's product
    // ids, merged with a Dictionary lookup in C#, avoids the translation risk entirely. This does
    // NOT reintroduce the in-memory-aggregation anti-pattern GetPosTopProductsAsync/
    // GetLossesByProductAsync accept elsewhere in this file (materializing an entire table's worth
    // of rows before grouping) -- both aggregates here run server-side; only two small,
    // already-collapsed result sets (bounded by catalog size, not by transaction volume) get
    // merged client-side.
    public async Task<WorstProductsDto> GetWorstProductsAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, int limit, CancellationToken ct = default)
    {
        // ── Base set: on-hand stock (Quantity > 0, excluding sold_out/archived batches) summed
        // per product, restricted to active items via a Contains-subquery against Items.IsActive
        // (same "Subquery-Contains join" shape GetLossesByProductAsync/GetPosTopProductsAsync
        // already use in this file) -- IsActive lives on Item, not ProductStock, so it can't be
        // filtered directly on the stock query itself.
        var activeItemIds = _db.Items.Where(i => i.IsActive);
        if (tenantId.HasValue)
            activeItemIds = activeItemIds.Where(i => i.TenantId == tenantId.Value);

        var stockQuery = _db.ProductStocks
            .Where(s => s.Quantity > 0 && s.Status != "sold_out" && s.Status != "archived");

        if (tenantId.HasValue)
            stockQuery = stockQuery.Where(s => s.TenantId == tenantId.Value);
        if (storeId.HasValue)
            stockQuery = stockQuery.Where(s => s.StoreId == storeId.Value);

        stockQuery = stockQuery.Where(s => activeItemIds.Select(i => i.Id).Contains(s.ProductId));

        // Scalar-only GroupBy+Sum (no navigation properties in the grouped select) -- the
        // translatable shape already proven in this file by GetLossesTrendAsync/
        // GetProductSalesTrendAsync's own SQL-side aggregates.
        var stockRows = await stockQuery
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, CurrentStock = g.Sum(s => s.Quantity) })
            .ToListAsync(ct);

        if (stockRows.Count == 0)
            return new WorstProductsDto(Products: []);

        var productIds = stockRows.Select(r => r.ProductId).ToList();

        var itemNames = await _db.Items
            .Where(i => productIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        // ── Sales side: same rollup shape as GetPosTopProductsAsync above, pre-filtered to just
        // this candidate pool's product ids -- unlike that method (which pulls every product sold
        // in the period into memory before grouping), the ProductId pre-filter here keeps the
        // materialized row count bounded by the stock candidate pool, not by sales volume, so the
        // in-memory GroupBy (needed for TransactionCount's distinct-transaction dedup, same as
        // GetPosTopProductsAsync) stays cheap.
        var fromDt = ToUtcStart(from);
        var toDt   = ToUtcEnd(to);
        var txQuery = BuildPosTransactionQuery(tenantId, storeId, fromDt, toDt);

        var salesRows = await _db.PosTransactionItems
            .Where(i => productIds.Contains(i.ProductId) && txQuery.Select(t => t.Id).Contains(i.TransactionId))
            .Select(i => new { i.ProductId, i.PriceFinal, i.Quantity, i.TransactionId })
            .ToListAsync(ct);

        var salesByProduct = salesRows
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => new
            {
                SalesRevenue     = g.Sum(i => i.PriceFinal * i.Quantity),
                UnitsSold        = g.Sum(i => i.Quantity),
                TransactionCount = g.Select(i => i.TransactionId).Distinct().Count()
            });

        // ── Merge: COALESCE missing sales to 0 (a product in the stock candidate pool with no
        // matching sales rows is exactly the "dead stock" case this endpoint exists to surface),
        // order ascending by revenue (true zero-revenue products first), then cap at `limit`.
        var products = stockRows
            .Select(r =>
            {
                var hasSales = salesByProduct.TryGetValue(r.ProductId, out var sales);
                return new WorstProductRowDto(
                    ProductId:        r.ProductId,
                    ProductName:      itemNames.GetValueOrDefault(r.ProductId, "Unknown"),
                    SalesRevenue:     hasSales ? sales!.SalesRevenue : 0m,
                    UnitsSold:        hasSales ? sales!.UnitsSold : 0m,
                    TransactionCount: hasSales ? sales!.TransactionCount : 0,
                    CurrentStock:     r.CurrentStock);
            })
            .OrderBy(p => p.SalesRevenue)
            .Take(limit)
            .ToList();

        return new WorstProductsDto(Products: products);
    }

    public async Task<PosCashierStatsDto> GetPosCashierStatsAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromDt = ToUtcStart(from);
        var toDt   = ToUtcEnd(to);

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

    // ── TASK-482: single-product sales trend ─────────────────────────────────

    public async Task<ProductSalesTrendDto?> GetProductSalesTrendAsync(
        Guid? tenantId, Guid? storeId, Guid productId, DateOnly from, DateOnly to, string groupBy, bool includeMargin, CancellationToken ct = default)
    {
        // Tenant-scoped existence check doubles as the ProductName/PricePurchase source. Null
        // here is the controller's 404 signal — mirrors ItemsController.GetById's nullable-DTO
        // convention (no matching Item in the caller's tenant scope). Same belt-and-suspenders
        // explicit tenantId filter (on top of RLS) every other method in this file already uses.
        var itemQuery = _db.Items.Where(i => i.Id == productId);
        if (tenantId.HasValue)
            itemQuery = itemQuery.Where(i => i.TenantId == tenantId.Value);

        var item = await itemQuery
            .Select(i => new { i.Name, i.PricePurchase })
            .FirstOrDefaultAsync(ct);

        if (item is null)
            return null;

        var fromDt = ToUtcStart(from);
        var toDt   = ToUtcEnd(to);
        var txQuery = BuildPosTransactionQuery(tenantId, storeId, fromDt, toDt);
        var resolvedGroupBy = groupBy == "week" ? "week" : "day";

        // SQL-side GroupBy — deliberately unlike GetPosTopProductsAsync above (which materializes
        // rows into memory before grouping, an accepted anti-pattern for that lower-cardinality
        // query, not to be copied here). The join+group+aggregate all run server-side, so only
        // the already-collapsed points (<=366 rows for a 1-year day range) ever cross into
        // memory. Filtering PosTransactionItems by ProductId first (the index's leading column)
        // is what lets this use TASK-479's idx_pos_transaction_items_product_covering
        // ("ProductId","TransactionId") INCLUDE ("Quantity","PriceFinal") — confirmed via
        // EXPLAIN ANALYZE against real dev data: Index Only Scan, Heap Fetches: 0 (see task log).
        //
        // EF.Functions has no DateTrunc/week helper in this Npgsql EF Core provider version
        // (verified by reflecting the installed 8.0.11 package: ILike/JsonContains/full-text/
        // network helpers exist, date truncation does not — EF.Functions.DateTrunc doesn't
        // compile). "day" instead relies on the provider's built-in DateTime.Date member
        // translation (confirmed via ToQueryString(): -> date_trunc('day', "CreatedAt", 'UTC')).
        // "week" inlines the same Monday-anchored ISO offset IsoWeekStart() below uses
        // (dow == 0 ? -6 : 1 - dow) as translatable DateTime member arithmetic instead of calling
        // that method directly (EF cannot translate a call to an arbitrary private C# method) —
        // confirmed translatable and correct against real data: every resulting week bucket key
        // landed exactly on a Monday when spot-checked, and day/week point sums both matched an
        // independently-computed ground-truth total exactly.
        var baseQuery = _db.PosTransactionItems
            .Where(i => i.ProductId == productId)
            .Join(txQuery, i => i.TransactionId, t => t.Id, (i, t) => new { i.Quantity, i.PriceFinal, t.CreatedAt });

        var grouped = resolvedGroupBy == "week"
            ? await baseQuery
                .GroupBy(x => x.CreatedAt.Date.AddDays((int)x.CreatedAt.DayOfWeek == 0 ? -6 : 1 - (int)x.CreatedAt.DayOfWeek))
                .Select(g => new
                {
                    Date = g.Key,
                    Revenue = g.Sum(x => x.PriceFinal * x.Quantity),
                    Quantity = g.Sum(x => x.Quantity),
                    // Item-row count within the bucket, not a distinct-transaction count — a
                    // single transaction with two line items for this product (e.g. two
                    // different batches) counts as 2 here. Matches this endpoint's own per-point
                    // granularity; unlike GetPosTopProductsAsync's TransactionCount, which
                    // explicitly de-dupes.
                    Transactions = g.Count()
                })
                .OrderBy(p => p.Date)
                .ToListAsync(ct)
            : await baseQuery
                .GroupBy(x => x.CreatedAt.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Revenue = g.Sum(x => x.PriceFinal * x.Quantity),
                    Quantity = g.Sum(x => x.Quantity),
                    Transactions = g.Count()
                })
                .OrderBy(p => p.Date)
                .ToListAsync(ct);

        // Margin (ADR-027): cheap second pass over the already-collapsed (<=366-row) points list
        // — does not reintroduce the in-memory-aggregation anti-pattern since the expensive
        // join/group/sum already happened in SQL above. Same null-vs-authorized /
        // null-vs-no-cost-on-file distinction as GetCategoryProductBreakdownAsync (TASK-481).
        var points = grouped
            .Select(p => new ProductSalesTrendPointDto(
                Date: DateOnly.FromDateTime(p.Date),
                Revenue: p.Revenue,
                Quantity: p.Quantity,
                TransactionCount: p.Transactions,
                MarginAmount: includeMargin && item.PricePurchase.HasValue
                    ? p.Revenue - p.Quantity * item.PricePurchase.Value
                    : null))
            .ToList();

        return new ProductSalesTrendDto(
            ProductId: productId,
            ProductName: item.Name,
            Points: points,
            GroupBy: resolvedGroupBy);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    // Npgsql rejects DateTime with Kind=Unspecified as a parameter for
    // timestamptz columns — date-range bounds must be explicitly UTC.
    private static DateTime ToUtcStart(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    private static DateTime ToUtcEnd(DateOnly date)   => date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

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
