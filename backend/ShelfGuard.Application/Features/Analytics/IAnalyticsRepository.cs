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

    // POS analytics
    Task<PosAnalyticsSummaryDto> GetPosSummaryAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<PosRevenueTrendDto> GetPosRevenueTrendAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, string groupBy, CancellationToken ct = default);
    Task<PosTopProductsDto> GetPosTopProductsAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, int limit, CancellationToken ct = default);
    Task<PosCashierStatsDto> GetPosCashierStatsAsync(Guid? tenantId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
