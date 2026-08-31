using ShelfGuard.Application.Features.Geo.Dtos;

namespace ShelfGuard.Application.Features.Geo;

/// <summary>
/// Read-only access to the Ukraine region taxonomy. Single source of truth:
/// <see cref="ShelfGuard.Domain.Constants.UkraineRegions"/>. Frontend and mobile consume this
/// via <c>GET /api/geo/regions</c> and never hardcode the list.
/// </summary>
public interface IGeoService
{
    /// <summary>All oblast-level units and major cities (oblasts first, then cities).</summary>
    IReadOnlyList<RegionDto> GetRegions();
}
