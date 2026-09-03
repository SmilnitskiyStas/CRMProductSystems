using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Data access for the global, provider-curated <c>platform_categories</c> table (B1/B2).
/// No TenantId, no RLS — every tenant reads the same rows; <see cref="ICategoryService"/>
/// narrows the tenant-facing list to the caller's <c>Tenant.BusinessType</c>, and the
/// provider CRUD (<c>ProviderCategoryService</c>) is the only writer.
/// </summary>
public interface ICategoryRepository
{
    /// <summary>
    /// Active categories only, ordered by name — the flat lookup list backing the catalog
    /// filter dropdown (TASK-632). No pagination: same reasoning as
    /// <c>ILocationRepository.GetAllAsync</c> — the category list is small.
    /// </summary>
    Task<List<PlatformCategory>> GetAllActiveAsync(CancellationToken ct = default);

    /// <summary>Full tree incl. inactive rows, ordered <c>SortOrder</c> then <c>Name</c> — the provider CRUD list.</summary>
    Task<List<PlatformCategory>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Case-insensitive <c>Name ILIKE '%term%'</c> typeahead over active categories (Phase 6e),
    /// ordered by name, capped at <paramref name="limit"/>. Blank <paramref name="term"/> returns
    /// the first <paramref name="limit"/> active categories. Each hit carries its parent's name
    /// (one self-join) and the item count for <paramref name="tenantId"/>'s own <c>items</c> in
    /// that category. Mirrors <c>AudienceBuilderRepository.SearchCategoriesAsync</c>.
    /// </summary>
    Task<IReadOnlyList<CategorySearchRow>> SearchActiveAsync(
        Guid tenantId, string? term, int limit, CancellationToken ct = default);

    Task<PlatformCategory?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>True when a category with this id exists and is <c>IsActive</c>. Backs the Item.CategoryId validation.</summary>
    Task<bool> ActiveExistsAsync(Guid id, CancellationToken ct = default);

    /// <summary>True when the category has at least one active direct child — blocks soft-delete.</summary>
    Task<bool> HasActiveChildrenAsync(Guid parentId, CancellationToken ct = default);

    /// <summary>
    /// Platform-wide count of <c>items</c> per non-null <c>CategoryId</c>, one grouped query.
    /// Provider connection carries <c>provider_bypass</c>, so this spans every tenant.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountItemsByCategoryAsync(CancellationToken ct = default);

    Task AddAsync(PlatformCategory category, CancellationToken ct = default);
    void Update(PlatformCategory category);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Repository row for <see cref="ICategoryRepository.SearchActiveAsync"/> (Phase 6e typeahead).</summary>
public sealed record CategorySearchRow(Guid Id, string Name, string? ParentName, int ItemCount);
