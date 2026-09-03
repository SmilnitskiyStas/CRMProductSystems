namespace ShelfGuard.Application.Features.Catalog.Dtos;

/// <summary>Flat lookup row for the catalog filter dropdown (TASK-632). No pagination, no parent/tree shape.</summary>
public sealed record CategoryDto(Guid Id, string Name, Guid? ParentId = null);

/// <summary>
/// One hit from the category typeahead (<c>GET /api/categories/search</c>, supplier-portal
/// expansion #8, Phase 6e). <see cref="ParentName"/> disambiguates same-named leaves across the
/// tree; <see cref="ItemCount"/> is the caller tenant's own catalog items in that category (0 for
/// a pure supplier tenant — harmless). Every active category is returned regardless of business
/// type (a supplier sells across verticals — plan decision).
/// </summary>
public sealed record CategorySearchResultDto(Guid Id, string Name, string? ParentName, int ItemCount);
