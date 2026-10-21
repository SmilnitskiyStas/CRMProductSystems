using ShelfGuard.Application.Features.Locations.Dtos;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Locations;

public interface IShelfInsightService
{
    /// <summary>Insights for every planogram item of one shelf section (null Items + error when the zone is not found).</summary>
    Task<(List<ShelfItemInsightDto>? Items, string? Error)> GetShelfInsightsAsync(
        Guid tenantId, Guid locationId, Guid zoneId, Guid shelfId, CancellationToken ct = default);

    /// <summary>Insights for already-loaded planogram rows (3 bulk queries total, regardless of row count).</summary>
    Task<List<ShelfItemInsightDto>> BuildAsync(
        Guid locationId, Guid zoneId, IReadOnlyList<ShelfItem> planogram, CancellationToken ct = default);
}
