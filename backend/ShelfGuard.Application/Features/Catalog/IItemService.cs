using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Catalog.Dtos;

namespace ShelfGuard.Application.Features.Catalog;

public interface IItemService
{
    // B2: uncategorized — true → only items with no category (overrides categoryId); a set
    // categoryId otherwise expands to that category's whole subtree. See IItemRepository.
    Task<List<ItemDto>> GetAllAsync(
        Guid tenantId,
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        bool? uncategorized = null,
        CancellationToken ct = default);

    // TASK-640: minPrice/maxPrice — see IItemRepository.GetPagedAsync. B2: uncategorized — see
    // GetAllAsync above. All appended at the very end (still before ct) so no pre-existing
    // parameter's positional index shifts for existing callers.
    Task<PagedResult<ItemDto>> GetPagedAsync(
        Guid tenantId,
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        string? search,
        IReadOnlyList<Guid>? ids,
        string? sortBy,
        bool? sortDescending,
        int page,
        int pageSize,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        bool? uncategorized = null,
        CancellationToken ct = default);

    Task<ItemDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<ItemDto?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);

    Task<(ItemDto? Product, string? Error)> CreateAsync(
        Guid tenantId,
        CreateProductRequest request,
        CancellationToken ct = default);

    Task<(ItemDto? Product, string? Error)> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<List<ProductSupplierSettingDto>> GetSuppliersAsync(Guid productId, CancellationToken ct = default);

    Task<(ProductSupplierSettingDto? Setting, string? Error)> AddSupplierAsync(
        Guid productId,
        Guid tenantId,
        AddProductSupplierRequest request,
        CancellationToken ct = default);

    Task<BarcodeProductLookupDto?> LookupByBarcodeExternalAsync(string barcode, CancellationToken ct);

    Task<(string? Url, string? Error)> UploadImageAsync(Guid itemId, Stream imageStream, string fileName, CancellationToken ct);
}
