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
    bool IsAvailable);

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
