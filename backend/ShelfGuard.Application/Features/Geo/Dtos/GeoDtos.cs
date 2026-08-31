namespace ShelfGuard.Application.Features.Geo.Dtos;

/// <summary>
/// A single Ukraine region served by <c>GET /api/geo/regions</c>. Mirrors
/// <see cref="ShelfGuard.Domain.Constants.RegionDefinition"/> — same Domain-constant →
/// Application-DTO mapping pattern as <c>MarketplaceService.GetItemCategories</c> /
/// <c>SupplierItemCategoryDto</c>.
/// </summary>
/// <param name="Code">
/// Stable region code. Oblast-level units use ISO 3166-2:UA (e.g. <c>UA-32</c> = Kyiv oblast,
/// <c>UA-30</c> = the city of Kyiv); cities use <c>{oblastCode}-{TRANSLIT}</c>
/// (e.g. <c>UA-18-ZHYTOMYR</c>).
/// </param>
/// <param name="NameUa">Ukrainian display name.</param>
/// <param name="Kind"><c>"oblast"</c> or <c>"city"</c>.</param>
/// <param name="ParentCode">Owning oblast code for a city; <c>null</c> for an oblast.</param>
public record RegionDto(string Code, string NameUa, string Kind, string? ParentCode);
