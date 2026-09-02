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
}
