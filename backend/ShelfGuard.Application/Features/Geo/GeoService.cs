using ShelfGuard.Application.Features.Geo.Dtos;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.Geo;

/// <summary>
/// Maps the static <see cref="UkraineRegions"/> registry to <see cref="RegionDto"/>.
/// No DB access — kept an injectable service anyway for thin-controller compliance and
/// testability, consistent with the other <c>Features/*</c> services.
/// </summary>
public sealed class GeoService : IGeoService
{
    public IReadOnlyList<RegionDto> GetRegions() =>
        UkraineRegions.All
            .Select(r => new RegionDto(r.Code, r.NameUa, r.Kind, r.ParentCode))
            .ToList();
}
