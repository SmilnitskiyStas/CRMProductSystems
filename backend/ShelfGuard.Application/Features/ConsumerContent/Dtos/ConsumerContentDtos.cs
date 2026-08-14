namespace ShelfGuard.Application.Features.ConsumerContent.Dtos;

/// <summary>
/// Consumer-facing banner shape (TASK-521, ConsumerContentController). Unlike the admin
/// BannerDto (Application.Features.Banners), Body/Terms are split back into arrays here —
/// this is the mobile handoff contract (see 521-to-mobile-developer handoff doc): the mock
/// data these replace (mobile/features/loyalty/news.ts ConsumerNewsItem) already shapes
/// body/terms as string[], one paragraph/bullet per element.
/// </summary>
public sealed record ConsumerBannerDto(
    Guid Id,
    string Title,
    string? Eyebrow,
    string Description,
    string[] Body,
    string[] Terms,
    string? ImageUrl,
    string Icon,
    string BackgroundColor,
    string AccentColor,
    string DetailMode,
    string? ExternalUrl,
    DateTime? ValidUntil,
    int SortOrder,
    IReadOnlyList<ConsumerBannerProductDto> Products
);

public sealed record ConsumerBannerProductDto(
    Guid Id,
    string Name,
    string? ImageUrl,
    string Unit,
    decimal? PriceRetail
);

/// <summary>Active promotional product — a read projection over Discount+Item (no new backend for Discount itself).</summary>
public sealed record ConsumerPromotionDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string? ImageUrl,
    string Unit,
    decimal DiscountPercent,
    decimal? PriceOriginal,
    decimal? PriceDiscounted,
    DateTime? ValidUntil
);

public sealed record ConsumerCatalogItemDto(
    Guid Id,
    string Name,
    string? ImageUrl,
    string Unit,
    decimal? PriceRetail,
    Guid? CategoryId,
    string? CategoryName,
    bool IsAvailableAtStore
);
