using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.MarketingAnalytics.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.MarketingAnalytics;

public sealed class MarketingAnalyticsService : IMarketingAnalyticsService
{
    private const int AffinityCandidatePoolSize = 200;
    private const int DefaultTopProductsLimit = 10;
    private const int DefaultExportMaxRows = 50_000;

    private readonly IMarketingAnalyticsRepository _repo;
    private readonly IMarketingAdvisor _advisor;
    private readonly IExcelExportService _excel;
    private readonly IActivityLogRepository _activityLogs;

    public MarketingAnalyticsService(
        IMarketingAnalyticsRepository repo,
        IMarketingAdvisor advisor,
        IExcelExportService excel,
        IActivityLogRepository activityLogs)
    {
        _repo = repo;
        _advisor = advisor;
        _excel = excel;
        _activityLogs = activityLogs;
    }

    public async Task<RfmOverviewDto> GetOverviewAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var population = await ClassifyAllAsync(tenantId, storeIds, from, to, ct);
        var baseCounts = await _repo.GetCustomerBaseCountsAsync(tenantId, storeIds, ct);

        var segments = RfmSegmentCatalog.AllKeysInPriorityOrder.Select(key =>
        {
            var group = population.BySegment[key];
            var revenue = group.Sum(r => r.PeriodRevenue);
            return new RfmSegmentSummaryDto(
                key,
                RfmSegmentCatalog.LabelUa(key),
                RfmSegmentCatalog.ShortDescriptionUa(key),
                group.Count,
                SharePercent(group.Count, population.TotalCustomerCount),
                revenue,
                SharePercent(revenue, population.TotalRevenue));
        }).ToList();

        // "Registered" is tenant-wide (Customer has no store association at all); "ever
        // purchased"/"no purchase" respect the store filter, same as everything else here —
        // a store-scoped dashboard reasonably shows "no purchase AT THESE STORES" even for a
        // customer who has purchase history elsewhere in the tenant.
        var noPurchaseCount = Math.Max(0, baseCounts.RegisteredCount - baseCounts.EverPurchasedCount);

        return new RfmOverviewDto(
            from,
            to,
            population.TotalCustomerCount,
            population.TotalRevenue,
            baseCounts.RegisteredCount,
            baseCounts.EverPurchasedCount,
            SharePercent(baseCounts.EverPurchasedCount, baseCounts.RegisteredCount),
            segments,
            new RfmNoPurchaseSummaryDto(noPurchaseCount, SharePercent(noPurchaseCount, baseCounts.RegisteredCount)),
            RfmFilterHash.Compute(tenantId, storeIds, from, to),
            DateTimeOffset.UtcNow);
    }

    public async Task<RfmSegmentDetailDto> GetSegmentDetailAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, RfmSegmentKey key,
        CancellationToken ct = default)
    {
        var (detail, _) = await BuildSegmentDetailAsync(tenantId, storeIds, from, to, key, ct);
        return detail;
    }

    public async Task<RfmAffinityResultDto> GetAffinityAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, RfmSegmentKey key,
        string productName, int topN = 10, CancellationToken ct = default)
    {
        var customerIds = await GetSegmentCustomerIdsAsync(tenantId, storeIds, from, to, key, ct);

        var rows = customerIds.Count == 0
            ? []
            : await _repo.GetAffinityAsync(tenantId, customerIds, storeIds, from, to, productName, AffinityCandidatePoolSize, topN, ct);

        var items = rows
            .Select(r => new RfmAffinityItemDto(r.ProductName, r.Lift, r.BothBuyersCount, r.ShareAmongAnchorBuyersPercent, r.ShareAmongSegmentPercent, r.Barcode))
            .ToList();

        return new RfmAffinityResultDto(key, productName, items, RfmFilterHash.Compute(tenantId, storeIds, from, to), DateTimeOffset.UtcNow);
    }

    public async Task<RfmBasketResultDto> GetBasketAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, RfmSegmentKey key,
        string productName, int topN = 10, CancellationToken ct = default)
    {
        var customerIds = await GetSegmentCustomerIdsAsync(tenantId, storeIds, from, to, key, ct);

        var rows = customerIds.Count == 0
            ? []
            : await _repo.GetBasketAsync(tenantId, customerIds, storeIds, from, to, productName, topN, ct);

        var items = rows
            .Select(r => new RfmBasketItemDto(
                r.ProductName,
                r.AnchorReceiptsCount > 0 ? Math.Round(r.BothReceiptsCount / (decimal)r.AnchorReceiptsCount * 100m, 2) : 0m,
                r.BothReceiptsCount,
                r.Barcode))
            .ToList();

        return new RfmBasketResultDto(key, productName, items, RfmFilterHash.Compute(tenantId, storeIds, from, to), DateTimeOffset.UtcNow);
    }

    public async Task<ExplainRfmSegmentResultDto> ExplainSegmentAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, RfmSegmentKey key,
        CancellationToken ct = default)
    {
        var (detail, recInput) = await BuildSegmentDetailAsync(tenantId, storeIds, from, to, key, ct);

        var context = new MarketingAdvisorContext(
            key,
            detail.LabelUa,
            recInput.CustomerCount,
            recInput.SharePercentOfPeriodCustomers,
            recInput.SharePercentOfPeriodRevenue,
            recInput.AverageRecencyDays,
            recInput.AverageLtv,
            recInput.TopProductName,
            detail.Recommendation.TriggerUa,
            detail.Recommendation.ActionUa,
            detail.Recommendation.OfferUa,
            detail.Recommendation.CautionUa);

        var result = await _advisor.ExplainAsync(context, ct);
        return new ExplainRfmSegmentResultDto(result.ExplanationUa, result.Model, result.TokensUsed);
    }

    public async Task<RfmExportResult> ExportSegmentAsync(
        Guid tenantId, Guid userId, ExportRfmFilterRequest request, CancellationToken ct = default)
    {
        var customerIds = await GetSegmentCustomerIdsAsync(tenantId, request.StoreIds, request.From, request.To, request.Key, ct);
        var customers = customerIds.Count == 0 ? [] : await _repo.GetExportCustomersAsync(tenantId, customerIds, ct);

        var result = BuildCustomerExcel($"rfm_{request.Key}_segment", customers, request.UnmaskPii);

        await LogExportAsync(
            tenantId, userId, "marketing_analytics.export_segment", request.Key,
            request.From, request.To, request.StoreIds, result, request.UnmaskPii, ct);

        return result;
    }

    public async Task<RfmExportResult> ExportProductBuyersAsync(
        Guid tenantId, Guid userId, ExportRfmProductRequest request, CancellationToken ct = default)
    {
        var segmentCustomerIds = await GetSegmentCustomerIdsAsync(tenantId, request.StoreIds, request.From, request.To, request.Key, ct);

        var buyerIds = segmentCustomerIds.Count == 0
            ? []
            : await _repo.GetProductBuyerCustomerIdsAsync(
                tenantId, segmentCustomerIds, request.StoreIds, request.From, request.To, request.ProductName, ct);

        var customers = buyerIds.Count == 0 ? [] : await _repo.GetExportCustomersAsync(tenantId, buyerIds, ct);
        var result = BuildCustomerExcel($"rfm_{request.Key}_product_buyers", customers, request.UnmaskPii);

        await LogExportAsync(
            tenantId, userId, "marketing_analytics.export_product_buyers", request.Key,
            request.From, request.To, request.StoreIds, result, request.UnmaskPii, ct,
            extraMeta: $"product={request.ProductName}");

        return result;
    }

    public async Task<RfmExportResult> ExportProductPairBuyersAsync(
        Guid tenantId, Guid userId, ExportRfmProductPairRequest request, CancellationToken ct = default)
    {
        var segmentCustomerIds = await GetSegmentCustomerIdsAsync(tenantId, request.StoreIds, request.From, request.To, request.Key, ct);

        var buyerIds = segmentCustomerIds.Count == 0
            ? []
            : await _repo.GetProductPairBuyerCustomerIdsAsync(
                tenantId, segmentCustomerIds, request.StoreIds, request.From, request.To,
                request.ProductName, request.PairedProductName, ct);

        var customers = buyerIds.Count == 0 ? [] : await _repo.GetExportCustomersAsync(tenantId, buyerIds, ct);
        var result = BuildCustomerExcel($"rfm_{request.Key}_product_pair_buyers", customers, request.UnmaskPii);

        await LogExportAsync(
            tenantId, userId, "marketing_analytics.export_product_pair_buyers", request.Key,
            request.From, request.To, request.StoreIds, result, request.UnmaskPii, ct,
            extraMeta: $"productA={request.ProductName}; productB={request.PairedProductName}");

        return result;
    }

    // ── Store migration (TASK-502) ───────────────────────────────────────────────────────────

    public async Task<StoreMigrationOverviewDto> GetStoreMigrationAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var flowRows = await _repo.GetStoreMigrationFlowsAsync(tenantId, storeIds, from, to, ct);
        var activeCount = await _repo.GetActivePeriodCustomerCountAsync(tenantId, storeIds, from, to, ct);

        var flows = flowRows
            .Select(f => new StoreMigrationFlowDto(f.FromStoreId, f.FromStoreName, f.ToStoreId, f.ToStoreName, f.CustomerCount, f.Revenue))
            .ToList();

        // Each migrated customer contributes to exactly one from→to cell (their unique
        // first-store/last-store pair for the period), so summing CustomerCount across every
        // cell double-counts nobody.
        var migratedCount = flows.Sum(f => f.CustomerCount);

        var netFlowByStore = flows
            .SelectMany(f => new[] { (StoreId: f.FromStoreId, Name: f.FromStoreName), (StoreId: f.ToStoreId, Name: f.ToStoreName) })
            .GroupBy(s => s.StoreId)
            .Select(g =>
            {
                var name = g.First().Name;
                var gained = flows.Where(f => f.ToStoreId == g.Key).Sum(f => f.CustomerCount);
                var lost = flows.Where(f => f.FromStoreId == g.Key).Sum(f => f.CustomerCount);
                return new StoreNetFlowDto(g.Key, name, gained, lost, gained - lost);
            })
            .OrderByDescending(n => n.Net)
            .ToList();

        return new StoreMigrationOverviewDto(
            activeCount,
            migratedCount,
            SharePercent(migratedCount, activeCount),
            flows,
            netFlowByStore,
            from,
            to);
    }

    public async Task<IReadOnlyList<StoreMigrationCustomerRowDto>> GetStoreMigrationCustomersAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, int limit, CancellationToken ct = default)
    {
        var rows = await _repo.GetStoreMigrationCustomersAsync(tenantId, storeIds, from, to, limit, ct);

        // On-screen table is masked-by-default with no unmask escape hatch — unmasking only
        // ever happens through the audited, capability-gated Excel export below.
        return rows
            .Select(r => new StoreMigrationCustomerRowDto(
                r.CustomerId, r.Name, PiiMasking.MaskPhone(r.Phone), PiiMasking.MaskEmail(r.Email),
                r.FromStoreId, r.FromStoreName, r.FromDate, r.ToStoreId, r.ToStoreName, r.ToDate,
                r.TransactionCountInPeriod, r.RevenueInPeriod))
            .ToList();
    }

    public async Task<RfmExportResult> ExportStoreMigrationAsync(
        Guid tenantId, Guid userId, ExportStoreMigrationRequest request, CancellationToken ct = default)
    {
        var rows = await _repo.GetStoreMigrationCustomersAsync(
            tenantId, request.StoreIds, request.From, request.To, DefaultExportMaxRows, ct);

        var result = BuildStoreMigrationExcel(rows, request.UnmaskPii);

        await LogExportAsync(
            tenantId, userId, "marketing_analytics.export_store_migration",
            request.From, request.To, request.StoreIds, result, request.UnmaskPii, ct);

        return result;
    }

    private RfmExportResult BuildStoreMigrationExcel(IReadOnlyList<StoreMigrationCustomerRow> customers, bool unmaskPii)
    {
        string[] headers =
        [
            "Ім'я", "Телефон", "Email",
            "Заклад (перша покупка)", "Дата першої покупки",
            "Заклад (остання покупка)", "Дата останньої покупки",
            "К-сть чеків", "Сума",
        ];

        var rows = customers
            .Select(c => (IReadOnlyList<object?>)new object?[]
            {
                c.Name,
                unmaskPii ? c.Phone : PiiMasking.MaskPhone(c.Phone),
                unmaskPii ? c.Email : PiiMasking.MaskEmail(c.Email),
                c.FromStoreName,
                c.FromDate.ToString("yyyy-MM-dd"),
                c.ToStoreName,
                c.ToDate.ToString("yyyy-MM-dd"),
                c.TransactionCountInPeriod,
                c.RevenueInPeriod,
            })
            .ToList();

        var excelResult = _excel.Export(new ExcelExportRequest("Міграція", headers, rows, DefaultExportMaxRows));
        var fileName = $"store_migration_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return new RfmExportResult(excelResult.FileBytes, fileName, excelResult.RowCount, excelResult.Truncated);
    }

    // ── shared internals ─────────────────────────────────────────────────────

    private sealed record ClassifiedPopulation(
        IReadOnlyDictionary<RfmSegmentKey, List<RfmScoredCustomerRow>> BySegment,
        int TotalCustomerCount,
        decimal TotalRevenue);

    /// <summary>Scores + classifies every customer who purchased in [from,to] (+ store filter)
    /// — the one place RfmSegmentClassifier is invoked. Recomputed on every call, never cached.</summary>
    private async Task<ClassifiedPopulation> ClassifyAllAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var scored = await _repo.GetScoredCustomersAsync(tenantId, storeIds, from, to, ct);

        var bySegment = RfmSegmentCatalog.AllKeysInPriorityOrder.ToDictionary(k => k, _ => new List<RfmScoredCustomerRow>());
        foreach (var row in scored)
        {
            var key = RfmSegmentClassifier.Classify(
                row.RecencyScore, row.FrequencyScore, row.MonetaryScore,
                row.FirstPurchaseDateEver, row.LifetimeReceiptCount, row.LastPurchaseDateEver,
                periodEnd: to);
            bySegment[key].Add(row);
        }

        return new ClassifiedPopulation(bySegment, scored.Count, scored.Sum(r => r.PeriodRevenue));
    }

    public async Task<IReadOnlyList<Guid>> ResolveSegmentCustomerIdsAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, RfmSegmentKey key, CancellationToken ct)
    {
        var population = await ClassifyAllAsync(tenantId, storeIds, from, to, ct);
        return population.BySegment[key].Select(r => r.CustomerId).ToList();
    }

    private async Task<IReadOnlyList<Guid>> GetSegmentCustomerIdsAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, RfmSegmentKey key, CancellationToken ct)
        => await ResolveSegmentCustomerIdsAsync(tenantId, storeIds, from, to, key, ct);

    /// <summary>Builds both the wire-facing detail DTO and the raw KPI input record the AI
    /// advisor needs — shared by GetSegmentDetailAsync and ExplainSegmentAsync so the two never
    /// disagree about what "the segment's numbers" are for the same filter+key.</summary>
    private async Task<(RfmSegmentDetailDto Detail, RfmRecommendationInputDto RecInput)> BuildSegmentDetailAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, RfmSegmentKey key, CancellationToken ct)
    {
        var population = await ClassifyAllAsync(tenantId, storeIds, from, to, ct);
        var group = population.BySegment[key];
        var customerIds = group.Select(r => r.CustomerId).ToList();

        IReadOnlyList<RfmTopProductRow> topProductRows = [];
        var behaviorRow = new RfmBehaviorRow(0, 0m, null, 0m, [], []);
        var ltvRow = new RfmLtvRow(0m, 0m);

        // An empty segment (QA checklist requirement) skips every downstream query rather than
        // calling the repository with a 0-length customer-id array — the repository methods
        // already guard for this too, but skipping avoids the round-trips entirely.
        if (customerIds.Count > 0)
        {
            topProductRows = await _repo.GetTopProductsAsync(tenantId, customerIds, storeIds, from, to, DefaultTopProductsLimit, ct);
            behaviorRow = await _repo.GetBehaviorAsync(tenantId, customerIds, storeIds, from, to, ct);
            ltvRow = await _repo.GetLtvAsync(tenantId, customerIds, storeIds, ct);
        }

        var topProducts = topProductRows
            .Select((r, idx) => new RfmTopProductDto(
                idx + 1, r.ProductName, SharePercent(r.UniqueCustomerCount, group.Count), r.UniqueCustomerCount, r.ReceiptCount, r.Barcode))
            .ToList();

        var peakDay = behaviorRow.ByDayOfWeek.OrderByDescending(d => d.ReceiptCount).FirstOrDefault();
        var peakHour = behaviorRow.ByHour.OrderByDescending(h => h.ReceiptCount).FirstOrDefault();

        var behavior = new RfmBehaviorDto(
            peakDay?.DayOfWeekIso,
            peakHour?.Hour,
            behaviorRow.ReceiptCount > 0 ? Math.Round(behaviorRow.TotalRevenue / behaviorRow.ReceiptCount, 2) : 0m,
            behaviorRow.ReceiptCount,
            group.Count > 0 ? Math.Round(behaviorRow.ReceiptCount / (decimal)group.Count, 2) : 0m,
            behaviorRow.LastVisit,
            behaviorRow.AverageRecencyDays,
            ltvRow.AverageLtv,
            ltvRow.TotalLtv,
            behaviorRow.ByDayOfWeek
                .Select(d => new RfmDayOfWeekShareDto(d.DayOfWeekIso, SharePercent(d.ReceiptCount, behaviorRow.ReceiptCount)))
                .ToList(),
            behaviorRow.ByHour
                .Select(h => new RfmHourShareDto(h.Hour, SharePercent(h.ReceiptCount, behaviorRow.ReceiptCount)))
                .ToList(),
            behaviorRow.ByHour
                .OrderByDescending(h => h.ReceiptCount)
                .Take(3)
                .Select(h => new RfmHourShareDto(h.Hour, SharePercent(h.ReceiptCount, behaviorRow.ReceiptCount)))
                .ToList());

        var recInput = new RfmRecommendationInputDto(
            group.Count,
            SharePercent(group.Count, population.TotalCustomerCount),
            SharePercent(group.Sum(r => r.PeriodRevenue), population.TotalRevenue),
            behaviorRow.AverageRecencyDays,
            ltvRow.AverageLtv,
            peakDay?.DayOfWeekIso,
            peakHour?.Hour,
            topProducts.Take(3).Select(p => p.ProductName).ToList(),
            topProducts.Count > 0 ? topProducts[0].ProductName : null,
            topProducts.Count > 0 ? topProducts[0].CoveragePercent : null);

        var recommendation = RecommendationTemplates.Build(key, recInput);

        var detail = new RfmSegmentDetailDto(
            key,
            RfmSegmentCatalog.LabelUa(key),
            RfmSegmentCatalog.ShortDescriptionUa(key),
            group.Count,
            topProducts,
            behavior,
            recommendation,
            RfmFilterHash.Compute(tenantId, storeIds, from, to),
            DateTimeOffset.UtcNow);

        return (detail, recInput);
    }

    private RfmExportResult BuildCustomerExcel(string fileNamePrefix, IReadOnlyList<RfmExportCustomerRow> customers, bool unmaskPii)
    {
        string[] headers = ["Ім'я", "Телефон", "Email", "Кількість замовлень (за весь час)", "Сума покупок (за весь час)"];

        var rows = customers
            .Select(c => (IReadOnlyList<object?>)new object?[]
            {
                c.Name,
                unmaskPii ? c.Phone : PiiMasking.MaskPhone(c.Phone),
                unmaskPii ? c.Email : PiiMasking.MaskEmail(c.Email),
                c.TotalOrders,
                c.TotalSpent,
            })
            .ToList();

        var excelResult = _excel.Export(new ExcelExportRequest("RFM", headers, rows, DefaultExportMaxRows));
        var fileName = $"{fileNamePrefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return new RfmExportResult(excelResult.FileBytes, fileName, excelResult.RowCount, excelResult.Truncated);
    }

    private async Task LogExportAsync(
        Guid tenantId, Guid userId, string action, RfmSegmentKey key, DateOnly from, DateOnly to,
        IReadOnlyList<Guid>? storeIds, RfmExportResult result, bool unmaskedPii, CancellationToken ct,
        string? extraMeta = null)
    {
        var storesLabel = storeIds is { Count: > 0 } ? string.Join(",", storeIds) : "all";
        var meta =
            $"segment={key}; from={from:yyyy-MM-dd}; to={to:yyyy-MM-dd}; stores={storesLabel}; " +
            $"rows={result.RowCount}; truncated={result.Truncated}; piiMasked={!unmaskedPii}" +
            (extraMeta is null ? string.Empty : $"; {extraMeta}");

        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = tenantId,
            UserId = userId,
            Action = action,
            EntityType = "marketing_analytics_export",
            Meta = meta,
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);
    }

    /// <summary>Overload for exports with no RFM segment concept (store migration) — same audit
    /// shape as the segment-keyed overload above, minus the <c>segment=</c> meta field.</summary>
    private async Task LogExportAsync(
        Guid tenantId, Guid userId, string action, DateOnly from, DateOnly to,
        IReadOnlyList<Guid>? storeIds, RfmExportResult result, bool unmaskedPii, CancellationToken ct,
        string? extraMeta = null)
    {
        var storesLabel = storeIds is { Count: > 0 } ? string.Join(",", storeIds) : "all";
        var meta =
            $"from={from:yyyy-MM-dd}; to={to:yyyy-MM-dd}; stores={storesLabel}; " +
            $"rows={result.RowCount}; truncated={result.Truncated}; piiMasked={!unmaskedPii}" +
            (extraMeta is null ? string.Empty : $"; {extraMeta}");

        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = tenantId,
            UserId = userId,
            Action = action,
            EntityType = "marketing_analytics_export",
            Meta = meta,
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);
    }

    // MaskPhone/MaskEmail moved to PiiMasking.cs (TASK-420, clean move — see that file's doc
    // comment) so Фаза 2's PriceSegmentsService can share the exact same masking rules.

    private static decimal SharePercent(int numerator, int denominator) =>
        denominator <= 0 ? 0m : Math.Round(numerator / (decimal)denominator * 100m, 2);

    private static decimal SharePercent(decimal numerator, decimal denominator) =>
        denominator <= 0 ? 0m : Math.Round(numerator / denominator * 100m, 2);
}
