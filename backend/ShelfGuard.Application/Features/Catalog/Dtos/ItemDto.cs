namespace ShelfGuard.Application.Features.Catalog.Dtos;

public sealed record ItemDto(
    Guid Id,
    string? Barcode,
    string Name,
    Guid? CategoryId,
    string? CategoryName,
    Guid? SegmentId,
    string? SegmentName,
    string Unit,
    string ManagementType,
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
    DateTime CreatedAt
);

public sealed record CreateProductRequest(
    string Name,
    string? Barcode,
    Guid? CategoryId,
    Guid? SegmentId,
    string Unit,
    string ManagementType,
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
    string? ImageUrl
);

public sealed record UpdateProductRequest(
    string Name,
    string? Barcode,
    Guid? CategoryId,
    Guid? SegmentId,
    string Unit,
    string ManagementType,
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
    bool IsActive
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
