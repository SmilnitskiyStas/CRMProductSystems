using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>See <see cref="IMobileConfigVersionHistoryService"/>.</summary>
public sealed class MobileConfigVersionHistoryService : IMobileConfigVersionHistoryService
{
    private readonly IMobileConfigurationRepository _repo;

    public MobileConfigVersionHistoryService(IMobileConfigurationRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<MobileConfigVersionSummaryDto>> GetHistoryAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var versions = await _repo.GetVersionsForTenantAsync(tenantId, ct);
        return versions
            .Select(v => new MobileConfigVersionSummaryDto(v.Id, v.Version, v.Status, v.CreatedAt, v.PublishedAt, v.CreatedBy))
            .ToList();
    }
}
