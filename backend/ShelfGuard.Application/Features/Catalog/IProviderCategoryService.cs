using ShelfGuard.Application.Features.Catalog.Dtos;

namespace ShelfGuard.Application.Features.Catalog;

/// <summary>
/// Provider-only CRUD over the global <c>platform_categories</c> catalogue (B2). Backs
/// <c>api/provider/categories</c>. Returns the full tree incl. inactive rows; every write is
/// validated against the business-type allow-list and the parent tree is kept acyclic.
/// </summary>
public interface IProviderCategoryService
{
    Task<List<PlatformCategoryDto>> GetAllAsync(CancellationToken ct = default);

    Task<(PlatformCategoryDto? Dto, string? Error)> CreateAsync(
        CreatePlatformCategoryRequest request, CancellationToken ct = default);

    Task<(PlatformCategoryDto? Dto, string? Error)> UpdateAsync(
        Guid id, UpdatePlatformCategoryRequest request, CancellationToken ct = default);

    /// <summary>Soft-delete (<c>IsActive = false</c>). Error string; null on success.</summary>
    Task<string?> DeleteAsync(Guid id, CancellationToken ct = default);
}
