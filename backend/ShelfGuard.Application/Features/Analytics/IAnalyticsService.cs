using ShelfGuard.Application.Features.Analytics.Dtos;

namespace ShelfGuard.Application.Features.Analytics;

public interface IAnalyticsService
{
    Task<ExpirySummaryDto> GetExpirySummaryAsync(Guid? tenantId, Guid? storeId, bool network, CancellationToken ct = default);
    Task<WriteOffAnalyticsDto> GetWriteOffAnalyticsAsync(Guid? tenantId, Guid? storeId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<MovementAnalyticsDto> GetMovementAnalyticsAsync(Guid? tenantId, Guid? storeId, string? type, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<IReadOnlyList<ZoneAnalyticsDto>> GetByZoneAsync(Guid? tenantId, Guid? storeId, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryAnalyticsDto>> GetByCategoryAsync(Guid? tenantId, Guid? storeId, CancellationToken ct = default);
    Task<LossesDto> GetLossesAsync(Guid? tenantId, Guid? storeId, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    // ── TASK-481: category/losses product drill-down ────────────────────────
    Task<CategoryProductBreakdownDto> GetCategoryProductBreakdownAsync(Guid? tenantId, Guid? storeId, Guid? categoryId, DateOnly from, DateOnly to, bool includeMargin, CancellationToken ct = default);
    Task<LossesByProductDto> GetLossesByProductAsync(Guid? tenantId, Guid? storeId, string? reason, DateOnly from, DateOnly to, CancellationToken ct = default);

    // ── TASK-489: losses/write-offs trend over time ──────────────────────────
    Task<LossesTrendDto> GetLossesTrendAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, string groupBy, CancellationToken ct = default);

    // POS analytics
    Task<PosAnalyticsSummaryDto> GetPosSummaryAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<PosRevenueTrendDto> GetPosRevenueTrendAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, string groupBy, CancellationToken ct = default);
    Task<PosTopProductsDto> GetPosTopProductsAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, int limit, CancellationToken ct = default);
    Task<PosCashierStatsDto> GetPosCashierStatsAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default);

    // ── TASK-482: single-product sales trend ─────────────────────────────────
    // Null return means productId did not resolve to a real Item in the caller's tenant scope
    // (controller 404s), mirroring IItemService.GetByIdAsync's nullable-DTO convention.
    Task<ProductSalesTrendDto?> GetProductSalesTrendAsync(Guid? tenantId, Guid? storeId, Guid productId, DateOnly from, DateOnly to, string groupBy, bool includeMargin, CancellationToken ct = default);

    // ── TASK-490: worst-performing products / dead stock ─────────────────────
    Task<WorstProductsDto> GetWorstProductsAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, int limit, CancellationToken ct = default);

    // ── TASK-336: period comparison ─────────────────────────────────────────

    /// <summary>Dashboard week-over-week KPI: sales count, revenue, write-off loss (last 7 days vs prior 7 days).</summary>
    Task<WeeklyKpiDto> GetWeeklyKpiAsync(Guid? tenantId, Guid? storeId, CancellationToken ct = default);

    /// <summary>
    /// Live current expiry-summary counts vs. a persisted snapshot from `compareWeeksAgo` weeks back.
    /// Previous is null if no snapshot exists for that date (e.g. within 7 days of deploy).
    /// </summary>
    Task<ExpirySummaryComparisonDto> GetExpirySummaryComparisonAsync(Guid? tenantId, Guid? storeId, int compareWeeksAgo, CancellationToken ct = default);

    Task<WriteOffsComparisonDto> GetWriteOffAnalyticsComparisonAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, DateOnly compareFrom, DateOnly compareTo, CancellationToken ct = default);
    Task<LossesComparisonDto> GetLossesComparisonAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, DateOnly compareFrom, DateOnly compareTo, CancellationToken ct = default);
    Task<PosSummaryComparisonDto> GetPosSummaryComparisonAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, DateOnly compareFrom, DateOnly compareTo, CancellationToken ct = default);
    Task<PosRevenueTrendComparisonDto> GetPosRevenueTrendComparisonAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, string groupBy, DateOnly compareFrom, DateOnly compareTo, CancellationToken ct = default);
}
