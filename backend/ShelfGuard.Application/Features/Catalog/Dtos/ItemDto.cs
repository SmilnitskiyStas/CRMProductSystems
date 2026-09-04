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
    string? DefaultReimbursementType,
    decimal? DefaultReimbursementValue,
    string? ImageUrl,
    bool IsActive,
    DateTime CreatedAt,
    string? Manufacturer,
    string? CountryOrigin,
    string PerishabilityClass,
    // Slice 3 — promo highlight on the catalog table. Null unless the item has a running or
    // near-future promo. PromoState/PromoStartsAt/PromoDiscountPercent are populated by BOTH the
    // paged catalog list (Slice 3) and single-item GetByIdAsync (Slice 5, product detail banner) —
    // each endpoint resolves them with its own query, see ItemRepository.GetPromoStatesAsync vs
    // GetPromoDetailAsync.
    string? PromoState = null,           // "active" | "upcoming"
    DateTime? PromoStartsAt = null,      // set for "upcoming"
    decimal? PromoDiscountPercent = null,
    // Slice 5 — real order-formula forecast (×K) for the product-page banner. Only populated by
    // single-item GetByIdAsync; null on the paged catalog list (never queried there) and null
    // whenever no *applied* PromoCannibalization row exists for this product's own promo yet.
    decimal? PromoOrderCoefficient = null,
    // Slice 4c — the nightly replenishment engine's suggestion, rolled up as MAX across stores.
    // Null until the engine has written a product_buffer row for the item. Only populated by the
    // paged / list catalog endpoints (unlike the promo fields above, this is NOT also loaded by
    // single-item GetByIdAsync — the product form reads it off the list row it was opened from).
    decimal? SuggestedMinStock = null,
    decimal? SuggestedMaxStock = null,
    decimal? SuggestedSafetyBuffer = null,
    decimal? SuggestedAduEffective = null,
    DateTime? BufferCalculatedAt = null
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
    string? CountryOrigin,
    string? PerishabilityClass,
    /// <summary>
    /// Lineage pointer (TASK-597): set when this Item is being auto-provisioned from a
    /// marketplace SupplierItem at order time. Null for every other creation path (manual entry,
    /// barcode-lookup import, Pchilka import). See Item.SourceSupplierItemId.
    /// </summary>
    Guid? SourceSupplierItemId = null
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
    string? CountryOrigin,
    string? PerishabilityClass
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
