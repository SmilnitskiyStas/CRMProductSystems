using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Catalog;

public sealed class ItemService : IItemService
{
    private readonly IItemRepository _repo;

    public ItemService(IItemRepository repo) => _repo = repo;

    public async Task<List<ItemDto>> GetAllAsync(
        Guid tenantId,
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        CancellationToken ct = default)
    {
        var products = await _repo.GetAllAsync(categoryId, segmentId, managementType, ct);
        return products.Select(ToDto).ToList();
    }

    public async Task<ItemDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _repo.GetByIdAsync(id, ct);
        return product is null ? null : ToDto(product);
    }

    public async Task<ItemDto?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        var product = await _repo.GetByBarcodeAsync(barcode, ct);
        return product is null ? null : ToDto(product);
    }

    public async Task<(ItemDto? Product, string? Error)> CreateAsync(
        Guid tenantId,
        CreateProductRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Product name is required.");

        if (!IsValidManagementType(request.ManagementType))
            return (null, $"Invalid management type '{request.ManagementType}'. Valid values: MTS, MTO, NA, NM.");

        var product = new Item
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Barcode = request.Barcode?.Trim(),
            CategoryId = request.CategoryId,
            SegmentId = request.SegmentId,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "шт" : request.Unit.Trim(),
            ManagementType = request.ManagementType.ToUpperInvariant(),
            MinStock = request.MinStock,
            MaxStock = request.MaxStock,
            SafetyBuffer = request.SafetyBuffer,
            StorageTempMin = request.StorageTempMin,
            StorageTempMax = request.StorageTempMax,
            ShelfLifeDays = request.ShelfLifeDays,
            DefaultSupplierId = request.DefaultSupplierId,
            VatRate = request.VatRate,
            PricePurchase = request.PricePurchase,
            PriceRetail = request.PriceRetail,
            ImageUrl = request.ImageUrl,
        };

        await _repo.AddAsync(product, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(product), null);
    }

    public async Task<(ItemDto? Product, string? Error)> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        CancellationToken ct = default)
    {
        var product = await _repo.GetByIdAsync(id, ct);
        if (product is null)
            return (null, "Product not found.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Product name is required.");

        if (!IsValidManagementType(request.ManagementType))
            return (null, $"Invalid management type '{request.ManagementType}'. Valid values: MTS, MTO, NA, NM.");

        product.Name = request.Name.Trim();
        product.Barcode = request.Barcode?.Trim();
        product.CategoryId = request.CategoryId;
        product.SegmentId = request.SegmentId;
        product.Unit = string.IsNullOrWhiteSpace(request.Unit) ? "шт" : request.Unit.Trim();
        product.ManagementType = request.ManagementType.ToUpperInvariant();
        product.MinStock = request.MinStock;
        product.MaxStock = request.MaxStock;
        product.SafetyBuffer = request.SafetyBuffer;
        product.StorageTempMin = request.StorageTempMin;
        product.StorageTempMax = request.StorageTempMax;
        product.ShelfLifeDays = request.ShelfLifeDays;
        product.DefaultSupplierId = request.DefaultSupplierId;
        product.VatRate = request.VatRate;
        product.PricePurchase = request.PricePurchase;
        product.PriceRetail = request.PriceRetail;
        product.ImageUrl = request.ImageUrl;
        product.IsActive = request.IsActive;

        _repo.Update(product);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(product), null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _repo.GetByIdAsync(id, ct);
        if (product is null) return false;

        product.IsActive = false;
        _repo.Update(product);
        await _repo.SaveChangesAsync(ct);

        return true;
    }

    public async Task<List<ProductSupplierSettingDto>> GetSuppliersAsync(Guid productId, CancellationToken ct = default)
    {
        var settings = await _repo.GetSupplierSettingsAsync(productId, ct);
        return settings.Select(ToSupplierDto).ToList();
    }

    public async Task<(ProductSupplierSettingDto? Setting, string? Error)> AddSupplierAsync(
        Guid productId,
        Guid tenantId,
        AddProductSupplierRequest request,
        CancellationToken ct = default)
    {
        var product = await _repo.GetByIdAsync(productId, ct);
        if (product is null)
            return (null, "Product not found.");

        var exists = await _repo.SupplierSettingExistsAsync(productId, request.SupplierId, ct);
        if (exists)
            return (null, "Supplier setting for this product already exists.");

        if (request.Moq <= 0)
            return (null, "MOQ must be greater than 0.");

        if (request.Usq <= 0)
            return (null, "USQ must be greater than 0.");

        var setting = new ProductSupplierSetting
        {
            TenantId = tenantId,
            ProductId = productId,
            SupplierId = request.SupplierId,
            Moq = request.Moq,
            Usq = request.Usq,
            PricePurchase = request.PricePurchase,
            DeliveryDays = request.DeliveryDays,
            IsPrimary = request.IsPrimary,
        };

        await _repo.AddSupplierSettingAsync(setting, ct);
        await _repo.SaveChangesAsync(ct);

        // Reload with navigation to get supplier name
        var settings = await _repo.GetSupplierSettingsAsync(productId, ct);
        var created = settings.FirstOrDefault(s => s.Id == setting.Id);

        return (created is null ? ToSupplierDtoMinimal(setting) : ToSupplierDto(created), null);
    }

    // ── mapping ────────────────────────────────────────────────────────────

    private static ItemDto ToDto(Item p) => new(
        p.Id,
        p.Barcode,
        p.Name,
        p.CategoryId,
        p.Category?.Name,
        p.SegmentId,
        p.Segment?.Name,
        p.Unit,
        p.ManagementType,
        p.MinStock,
        p.MaxStock,
        p.SafetyBuffer,
        p.StorageTempMin,
        p.StorageTempMax,
        p.ShelfLifeDays,
        p.DefaultSupplierId,
        p.DefaultSupplier?.Name,
        p.VatRate,
        p.PricePurchase,
        p.PriceRetail,
        p.ImageUrl,
        p.IsActive,
        p.CreatedAt
    );

    private static ProductSupplierSettingDto ToSupplierDto(ProductSupplierSetting s) => new(
        s.Id,
        s.SupplierId,
        s.Supplier?.Name ?? string.Empty,
        s.Moq,
        s.Usq,
        s.PricePurchase,
        s.DeliveryDays,
        s.IsPrimary,
        s.IsActive
    );

    private static ProductSupplierSettingDto ToSupplierDtoMinimal(ProductSupplierSetting s) => new(
        s.Id,
        s.SupplierId,
        string.Empty,
        s.Moq,
        s.Usq,
        s.PricePurchase,
        s.DeliveryDays,
        s.IsPrimary,
        s.IsActive
    );

    private static bool IsValidManagementType(string type) =>
        type is "MTS" or "MTO" or "NA" or "NM";
}
