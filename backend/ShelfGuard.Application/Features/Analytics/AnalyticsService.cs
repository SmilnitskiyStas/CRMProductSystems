using ShelfGuard.Application.Features.Analytics.Dtos;

namespace ShelfGuard.Application.Features.Analytics;

public sealed class AnalyticsService : IAnalyticsService
{
    private readonly IAnalyticsRepository _repo;

    public AnalyticsService(IAnalyticsRepository repo) => _repo = repo;

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
}
