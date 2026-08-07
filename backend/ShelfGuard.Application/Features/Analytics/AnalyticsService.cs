using ShelfGuard.Application.Features.Analytics.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Analytics;

public sealed class AnalyticsService : IAnalyticsService
{
    private readonly IAnalyticsRepository _repo;
    private readonly IStockStatusSnapshotRepository _snapshots;

    public AnalyticsService(IAnalyticsRepository repo, IStockStatusSnapshotRepository snapshots)
    {
        _repo = repo;
        _snapshots = snapshots;
    }

    public Task<ExpirySummaryDto> GetExpirySummaryAsync(Guid? tenantId, Guid? storeId, bool network, CancellationToken ct = default)
        => _repo.GetExpirySummaryAsync(tenantId, storeId, network, ct);

    public Task<WriteOffAnalyticsDto> GetWriteOffAnalyticsAsync(Guid? tenantId, Guid? storeId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        => _repo.GetWriteOffAnalyticsAsync(tenantId, storeId, from, to, ct);

    public Task<MovementAnalyticsDto> GetMovementAnalyticsAsync(Guid? tenantId, Guid? storeId, string? type, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        => _repo.GetMovementAnalyticsAsync(tenantId, storeId, type, from, to, ct);

    public Task<IReadOnlyList<ZoneAnalyticsDto>> GetByZoneAsync(Guid? tenantId, Guid? storeId, CancellationToken ct = default)
        => _repo.GetByZoneAsync(tenantId, storeId, ct);

    public Task<IReadOnlyList<CategoryAnalyticsDto>> GetByCategoryAsync(Guid? tenantId, Guid? storeId, CancellationToken ct = default)
        => _repo.GetByCategoryAsync(tenantId, storeId, ct);

    public Task<LossesDto> GetLossesAsync(Guid? tenantId, Guid? storeId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        => _repo.GetLossesAsync(tenantId, storeId, from, to, ct);

    // ── TASK-481: category/losses product drill-down ─────────────────────────

    public Task<CategoryProductBreakdownDto> GetCategoryProductBreakdownAsync(Guid? tenantId, Guid? storeId, Guid? categoryId, DateOnly from, DateOnly to, bool includeMargin, CancellationToken ct = default)
        => _repo.GetCategoryProductBreakdownAsync(tenantId, storeId, categoryId, from, to, includeMargin, ct);

    public Task<LossesByProductDto> GetLossesByProductAsync(Guid? tenantId, Guid? storeId, string? reason, DateOnly from, DateOnly to, CancellationToken ct = default)
        => _repo.GetLossesByProductAsync(tenantId, storeId, reason, from, to, ct);

    public Task<PosAnalyticsSummaryDto> GetPosSummaryAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => _repo.GetPosSummaryAsync(tenantId, storeId, from, to, ct);

    public Task<PosRevenueTrendDto> GetPosRevenueTrendAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, string groupBy, CancellationToken ct = default)
        => _repo.GetPosRevenueTrendAsync(tenantId, storeId, from, to, groupBy, ct);

    public Task<PosTopProductsDto> GetPosTopProductsAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, int limit, CancellationToken ct = default)
        => _repo.GetPosTopProductsAsync(tenantId, storeId, from, to, limit, ct);

    public Task<PosCashierStatsDto> GetPosCashierStatsAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => _repo.GetPosCashierStatsAsync(tenantId, storeId, from, to, ct);

    // ── TASK-482: single-product sales trend ─────────────────────────────────

    public Task<ProductSalesTrendDto?> GetProductSalesTrendAsync(Guid? tenantId, Guid? storeId, Guid productId, DateOnly from, DateOnly to, string groupBy, bool includeMargin, CancellationToken ct = default)
        => _repo.GetProductSalesTrendAsync(tenantId, storeId, productId, from, to, groupBy, includeMargin, ct);

    // ── TASK-336: period comparison ─────────────────────────────────────────

    public async Task<WeeklyKpiDto> GetWeeklyKpiAsync(Guid? tenantId, Guid? storeId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentFrom = today.AddDays(-6);
        var previousFrom = currentFrom.AddDays(-7);
        var previousTo = today.AddDays(-7);

        var currentPos = await _repo.GetPosSummaryAsync(tenantId, storeId, currentFrom, today, ct);
        var previousPos = await _repo.GetPosSummaryAsync(tenantId, storeId, previousFrom, previousTo, ct);

        var currentWriteOffs = await _repo.GetWriteOffAnalyticsAsync(tenantId, storeId, currentFrom, today, ct);
        var previousWriteOffs = await _repo.GetWriteOffAnalyticsAsync(tenantId, storeId, previousFrom, previousTo, ct);

        return new WeeklyKpiDto(
            Sales: PeriodMetricDto.Of(currentPos.TransactionCount, previousPos.TransactionCount),
            Revenue: PeriodMetricDto.Of(currentPos.TotalRevenue, previousPos.TotalRevenue),
            WriteOffLoss: PeriodMetricDto.Of(currentWriteOffs.TotalLoss, previousWriteOffs.TotalLoss)
        );
    }

    public async Task<ExpirySummaryComparisonDto> GetExpirySummaryComparisonAsync(
        Guid? tenantId, Guid? storeId, int compareWeeksAgo, CancellationToken ct = default)
    {
        var current = await GetExpirySummaryAsync(tenantId, storeId, network: !storeId.HasValue, ct);

        // Snapshots are per-tenant; a cross-tenant provider call (tenantId == null) has no
        // single snapshot row to compare against, so there's nothing meaningful to return.
        if (tenantId is null)
            return new ExpirySummaryComparisonDto(current, null);

        var weeksAgo = compareWeeksAgo < 1 ? 1 : compareWeeksAgo;
        var compareDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7 * weeksAgo);

        ExpirySummaryDto? previous;
        if (storeId.HasValue)
        {
            var snapshot = await _snapshots.GetAsync(tenantId.Value, storeId.Value, compareDate, ct);
            previous = snapshot is null ? null : MapSnapshot(snapshot);
        }
        else
        {
            var snapshots = await _snapshots.GetByTenantAndDateAsync(tenantId.Value, compareDate, ct);
            previous = snapshots.Count == 0 ? null : MapSnapshots(snapshots);
        }

        return new ExpirySummaryComparisonDto(current, previous);
    }

    public async Task<WriteOffsComparisonDto> GetWriteOffAnalyticsComparisonAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, DateOnly compareFrom, DateOnly compareTo, CancellationToken ct = default)
    {
        var current = await _repo.GetWriteOffAnalyticsAsync(tenantId, storeId, from, to, ct);
        var comparison = await _repo.GetWriteOffAnalyticsAsync(tenantId, storeId, compareFrom, compareTo, ct);
        return new WriteOffsComparisonDto(current, comparison, PercentChange(current.TotalLoss, comparison.TotalLoss));
    }

    public async Task<LossesComparisonDto> GetLossesComparisonAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, DateOnly compareFrom, DateOnly compareTo, CancellationToken ct = default)
    {
        var current = await _repo.GetLossesAsync(tenantId, storeId, from, to, ct);
        var comparison = await _repo.GetLossesAsync(tenantId, storeId, compareFrom, compareTo, ct);
        return new LossesComparisonDto(current, comparison, PercentChange(current.TotalLoss, comparison.TotalLoss));
    }

    public async Task<PosSummaryComparisonDto> GetPosSummaryComparisonAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, DateOnly compareFrom, DateOnly compareTo, CancellationToken ct = default)
    {
        var current = await _repo.GetPosSummaryAsync(tenantId, storeId, from, to, ct);
        var comparison = await _repo.GetPosSummaryAsync(tenantId, storeId, compareFrom, compareTo, ct);
        return new PosSummaryComparisonDto(
            current,
            comparison,
            PercentChange(current.TotalRevenue, comparison.TotalRevenue),
            PercentChange(current.TransactionCount, comparison.TransactionCount));
    }

    public async Task<PosRevenueTrendComparisonDto> GetPosRevenueTrendComparisonAsync(
        Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, string groupBy, DateOnly compareFrom, DateOnly compareTo, CancellationToken ct = default)
    {
        var current = await _repo.GetPosRevenueTrendAsync(tenantId, storeId, from, to, groupBy, ct);
        var comparison = await _repo.GetPosRevenueTrendAsync(tenantId, storeId, compareFrom, compareTo, groupBy, ct);
        return new PosRevenueTrendComparisonDto(
            current.Points, comparison.Points, current.GroupBy, from, to, compareFrom, compareTo);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static ExpirySummaryDto MapSnapshot(StockStatusSnapshot s) => new(
        Safe: s.SafeCount,
        Warning: s.WarningCount,
        Critical: s.CriticalCount,
        Expired: s.ExpiredCount,
        NeedsVerification: 0, // not tracked by the snapshot table (TASK-335)
        Total: s.SafeCount + s.WarningCount + s.CriticalCount + s.ExpiredCount,
        Stores: new[]
        {
            new ExpirySummaryStoreDto(s.StoreId, s.Store?.Name ?? "Unknown", s.SafeCount, s.WarningCount, s.CriticalCount, s.ExpiredCount)
        });

    private static ExpirySummaryDto MapSnapshots(IReadOnlyList<StockStatusSnapshot> snapshots) => new(
        Safe: snapshots.Sum(s => s.SafeCount),
        Warning: snapshots.Sum(s => s.WarningCount),
        Critical: snapshots.Sum(s => s.CriticalCount),
        Expired: snapshots.Sum(s => s.ExpiredCount),
        NeedsVerification: 0,
        Total: snapshots.Sum(s => s.SafeCount + s.WarningCount + s.CriticalCount + s.ExpiredCount),
        Stores: snapshots
            .Select(s => new ExpirySummaryStoreDto(s.StoreId, s.Store?.Name ?? "Unknown", s.SafeCount, s.WarningCount, s.CriticalCount, s.ExpiredCount))
            .ToList());

    private static decimal? PercentChange(decimal current, decimal previous) =>
        previous == 0m ? null : Math.Round((current - previous) / previous * 100m, 2);

    private static decimal? PercentChange(int current, int previous) =>
        PercentChange((decimal)current, (decimal)previous);
}
