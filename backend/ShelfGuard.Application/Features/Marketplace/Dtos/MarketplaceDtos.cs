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
    Dictionary<string, object?>? Attributes = null);

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
    Dictionary<string, object?>? Attributes = null);

/// <summary>Patch-semantics item update — only non-null fields are applied.</summary>
public record AdminUpdateSupplierItemDto(
    string? CustomName,
    decimal? Price,
    int? MinQty,
    string? Unit,
    bool? IsAvailable,
    string? Category = null,
    Dictionary<string, object?>? Attributes = null);

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
