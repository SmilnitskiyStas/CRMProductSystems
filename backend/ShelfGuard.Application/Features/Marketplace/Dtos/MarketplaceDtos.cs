namespace ShelfGuard.Application.Features.Marketplace.Dtos;

/// <summary>Compact supplier card for the public marketplace listing.</summary>
public record SupplierListItemDto(
    Guid Id,
    string Name,
    string? Region,
    string Plan,
    string[]? Categories,
    decimal? Rating,
    int? AvgDeliveryDays,
    bool IsPublic);

/// <summary>Full supplier profile. Premium fields are null for unauthenticated / free-plan callers.</summary>
public record SupplierProfileDto(
    Guid SupplierId,
    string SupplierName,
    string? Region,
    string[]? Categories,
    string? Website,
    string[]? DeliveryRegions,
    string? WorkingHours,
    string? PaymentTerms,
    bool IsPublic,
    string Plan,
    SupplierMetricsDto? Metrics);

public record SupplierMetricsDto(
    decimal? Rating,
    decimal? AvgDeliveryDays,
    decimal? OrderAccuracy,
    decimal? QualityScore,
    decimal? CancellationRate,
    decimal? ResponseTimeHours,
    DateTimeOffset UpdatedAt);

public record SupplierItemDto(
    Guid Id,
    Guid? ItemId,
    string? CustomName,
    string? ItemName,
    decimal? Price,
    int? MinQty,
    string? Unit,
    bool IsAvailable,
    string? Category = null,
    Dictionary<string, object?>? Attributes = null,
    string? Brand = null,
    string? Manufacturer = null,
    string? ManufacturerCountry = null,
    int? MaxQty = null,
    decimal? GrossWeightKg = null,
    decimal? HeightCm = null,
    decimal? DepthCm = null,
    decimal? WidthCm = null,
    IReadOnlyList<string>? Barcodes = null,
    IReadOnlyList<SupplierItemImageDto>? Images = null);

/// <summary>A single supplier-item image, ordered by SortOrder. Kind is 'main' | 'gallery'.</summary>
public record SupplierItemImageDto(string Url, string Kind, int SortOrder);

public record SupplierReviewCreateDto(int Rating, string? Comment);

public record SupplierReviewDto(
    Guid Id,
    int Rating,
    string? Comment,
    DateTimeOffset CreatedAt);

public record SupplierSearchDto(string ItemName, string? Region);

public record SupplierProfileUpdateDto(
    string? Region,
    string[]? Categories,
    string? Website,
    string[]? DeliveryRegions,
    string? WorkingHours,
    string? PaymentTerms,
    bool? IsPublic,
    string? Plan);

/// <summary>Paginated list response.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// ── Platform admin DTOs (TASK-275) ───────────────────────────────────────────

public record AdminCreateSupplierDto(
    string CompanyName,
    string? Region,
    string[]? Categories,
    string? Website,
    string[]? DeliveryRegions,
    string? WorkingHours,
    string? PaymentTerms,
    bool IsPublic,
    string Plan);

public record AdminAddSupplierItemDto(
    string CustomName,
    decimal? Price,
    int? MinQty,
    string? Unit,
    bool IsAvailable,
    string? Category = null,
    Dictionary<string, object?>? Attributes = null,
    string? Brand = null,
    string? Manufacturer = null,
    string? ManufacturerCountry = null,
    int? MaxQty = null,
    decimal? GrossWeightKg = null,
    decimal? HeightCm = null,
    decimal? DepthCm = null,
    decimal? WidthCm = null,
    /// <summary>Plain barcode strings. First = primary, rest = alternate. Null/blank/duplicate entries are skipped.</summary>
    List<string>? Barcodes = null,
    /// <summary>Plain image URLs. First = main, rest = gallery (SortOrder = list index). Null/blank entries are skipped.</summary>
    List<string>? ImageUrls = null);

/// <summary>Patch-semantics item update — only non-null fields are applied.
/// Barcodes/ImageUrls collections are only replaced when the corresponding list is
/// explicitly provided (non-null); left untouched otherwise.</summary>
public record AdminUpdateSupplierItemDto(
    string? CustomName,
    decimal? Price,
    int? MinQty,
    string? Unit,
    bool? IsAvailable,
    string? Category = null,
    Dictionary<string, object?>? Attributes = null,
    string? Brand = null,
    string? Manufacturer = null,
    string? ManufacturerCountry = null,
    int? MaxQty = null,
    decimal? GrossWeightKg = null,
    decimal? HeightCm = null,
    decimal? DepthCm = null,
    decimal? WidthCm = null,
    List<string>? Barcodes = null,
    List<string>? ImageUrls = null);

// ── Supplier cabinet (v4.1, TASK-284, ADR-016) ───────────────────────────────

/// <summary>
/// Cabinet profile update. Publish state is toggled via POST /profile/publish,
/// and plan is provider-managed — neither is editable here.
/// </summary>
public record CabinetProfileUpdateDto(
    string? Region,
    string[]? Categories,
    string? Website,
    string[]? DeliveryRegions,
    string? WorkingHours,
    string? PaymentTerms);

// ── Public reviews (v4.1, TASK-285) ──────────────────────────────────────────

/// <summary>Public review representation — reviewer exposed by display name only (no tenant id).</summary>
public record PublicSupplierReviewDto(
    Guid Id,
    int Rating,
    string? Comment,
    DateTimeOffset CreatedAt,
    string ReviewerName);

// ── AI Supplier Recommendation (TASK-223) ─────────────────────────────────────

public record AiRecommendRequestDto(
    string ItemName,
    string? Region,
    int? RequiredQty,
    string? Notes);

public record AiRecommendResultDto(
    List<SupplierRecommendationDto> Recommendations,
    string Prompt);

public record SupplierRecommendationDto(
    Guid SupplierId,
    string SupplierName,
    int Rank,
    decimal Score,
    string Reasoning,
    SupplierItemDto? MatchedItem,
    SupplierMetricsDto? Metrics);

// ── Item category registry (TASK-294, ADR-017 §4) ────────────────────────────

public record SupplierItemCategoryFieldDto(
    string Key,
    string LabelUa,
    string Type,
    bool Required,
    IReadOnlyList<string>? Options);

public record SupplierItemCategoryDto(
    string Key,
    string LabelUa,
    IReadOnlyList<SupplierItemCategoryFieldDto> Fields);
