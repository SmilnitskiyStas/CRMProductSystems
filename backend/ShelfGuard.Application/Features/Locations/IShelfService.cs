using ShelfGuard.Application.Features.Locations.Dtos;

namespace ShelfGuard.Application.Features.Locations;

public interface IShelfService
{
    Task<(List<ShelfItemDto>? Items, string? Error)> GetShelfItemsAsync(
        Guid tenantId, Guid locationId, Guid zoneId, Guid? shelfId, CancellationToken ct = default);

    /// <summary>Zone sections (from the plan JSON) with their planogram items — for pick-a-shelf UIs.</summary>
    Task<(List<ShelfSectionDto>? Sections, string? Error)> GetShelvesAsync(
        Guid tenantId, Guid locationId, Guid zoneId, CancellationToken ct = default);

    Task<(List<ShelfItemDto>? Items, string? Error)> SetShelfItemsAsync(
        Guid tenantId, Guid locationId, Guid zoneId, SetShelfItemsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Saves the photo, runs the vision advisor against the zone planogram (or one shelf section
    /// when <paramref name="shelfId"/> is set) and persists a <c>ShelfScan</c>. <c>StatusCode</c>
    /// carries the HTTP code for a failure (400/404/422/502/503).
    /// </summary>
    Task<(ShelfScanDto? Scan, string? Error, int? StatusCode)> CreateScanAsync(
        Guid tenantId, Guid? userId, Guid locationId, Guid zoneId, Guid? shelfId,
        Stream image, long length, string fileName, CancellationToken ct = default);

    Task<(List<ShelfScanDto>? Scans, string? Error)> GetScansAsync(
        Guid tenantId, Guid locationId, Guid zoneId, int limit, CancellationToken ct = default);

    Task<(ShelfScanDto? Scan, string? Error)> GetScanAsync(
        Guid tenantId, Guid locationId, Guid zoneId, Guid scanId, CancellationToken ct = default);
}
