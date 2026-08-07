using ShelfGuard.Application.Features.Analytics.Dtos;

namespace ShelfGuard.Application.Features.Analytics;

public interface IAnalyticsRepository
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
}
