using ShelfGuard.Application.Features.Catalog.Dtos;

namespace ShelfGuard.Application.Features.Catalog;

public interface ICategoryService
{
    /// <summary>
    /// Flat active category list for the catalog filter dropdown, narrowed to the caller's
    /// <c>Tenant.BusinessType</c> (B2). <paramref name="tenantId"/> null → provider/no-tenant
    /// session → every active category (no business-type filter).
    /// </summary>
    Task<List<CategoryDto>> GetAllAsync(Guid? tenantId, CancellationToken ct = default);

    /// <summary>
    /// Category typeahead (<c>GET /api/categories/search</c>, supplier-portal expansion #8,
    /// Phase 6e). Case-insensitive name match over <b>all</b> active categories — deliberately
    /// NOT business-type-filtered, since a supplier sells across verticals (plan decision).
    /// <paramref name="limit"/> is clamped to 1..50 (default 20). Each hit carries its parent
    /// name and the caller tenant's own item count in that category.
    /// </summary>
    Task<IReadOnlyList<CategorySearchResultDto>> SearchAsync(
        Guid tenantId, string? query, int limit, CancellationToken ct = default);
}
