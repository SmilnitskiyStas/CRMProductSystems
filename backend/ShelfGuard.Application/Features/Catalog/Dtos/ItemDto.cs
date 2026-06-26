namespace ShelfGuard.Application.Features.Catalog.Dtos;

public sealed record ItemDto(
    Guid Id,
    List<string> Barcodes,
    string Name,
    Guid? CategoryId,
    string? CategoryName,
    Guid? SegmentId,
    string? SegmentName,
    string Unit,
    string ManagementType,
    string ItemType,
    decimal MinStock,
    decimal MaxStock,
    decimal SafetyBuffer,
    decimal? StorageTempMin,
    decimal? StorageTempMax,
    int? ShelfLifeDays,
    Guid? DefaultSupplierId,
    string? DefaultSupplierName,
    decimal VatRate,
    decimal? PricePurchase,
    decimal? PriceRetail,
    string? ImageUrl,
    bool IsActive,
    DateTime CreatedAt,
    string? Manufacturer,
    string? CountryOrigin
);

public sealed record CreateProductRequest(
    string Name,
    List<string>? Barcodes,
    Guid? CategoryId,
    Guid? SegmentId,
    string Unit,
    string ManagementType,
    string? ItemType,
    decimal MinStock,
    decimal MaxStock,
    decimal SafetyBuffer,
    decimal? StorageTempMin,
    decimal? StorageTempMax,
    int? ShelfLifeDays,
    Guid? DefaultSupplierId,
    decimal VatRate,
    decimal? PricePurchase,
    decimal? PriceRetail,
    string? ImageUrl,
    string? Manufacturer,
    string? CountryOrigin
);

public sealed record UpdateProductRequest(
    string Name,
    List<string>? Barcodes,
    Guid? CategoryId,
    Guid? SegmentId,
    string Unit,
    string ManagementType,
    string? ItemType,
    decimal MinStock,
    decimal MaxStock,
    decimal SafetyBuffer,
    decimal? StorageTempMin,
    decimal? StorageTempMax,
    int? ShelfLifeDays,
    Guid? DefaultSupplierId,
    decimal VatRate,
    decimal? PricePurchase,
    decimal? PriceRetail,
    string? ImageUrl,
    bool IsActive,
    string? Manufacturer,
    string? CountryOrigin
);

public sealed record ProductSupplierSettingDto(
    Guid Id,
    Guid SupplierId,
    string SupplierName,
    decimal Moq,
    decimal Usq,
    decimal? PricePurchase,
    int DeliveryDays,
    bool IsPrimary,
    bool IsActive
);

public sealed record AddProductSupplierRequest(
    Guid SupplierId,
    decimal Moq,
    decimal Usq,
    decimal? PricePurchase,
    int DeliveryDays,
    bool IsPrimary
);
