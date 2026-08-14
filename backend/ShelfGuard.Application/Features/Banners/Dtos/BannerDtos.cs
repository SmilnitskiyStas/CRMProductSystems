namespace ShelfGuard.Application.Features.Banners.Dtos;

/// <summary>
/// Admin-facing banner shape. Body/Terms are the raw "\n"-joined text as stored on the entity
/// (a plain textarea round-trips this directly) — the consumer-facing read endpoint
/// (ConsumerContentController, TASK-521) is the one that splits these into string[] for mobile.
/// </summary>
public sealed record BannerDto(
    Guid Id,
    string Title,
    string? Eyebrow,
    string Description,
    string Body,
    string Terms,
    string? ImageUrl,
    string Icon,
    string BackgroundColor,
    string AccentColor,
    string DetailMode,
    string? ExternalUrl,
    DateTime ValidFrom,
    DateTime? ValidUntil,
    bool IsActive,
    bool IsCurrentlyActive,
    int SortOrder,
    IReadOnlyList<Guid> LocationIds,
    IReadOnlyList<Guid> ProductIds,
    int ViewCount,
    int ClickCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public sealed record CreateBannerRequest(
    string Title,
    string Description,
    string Body,
    string Terms,
    string Icon,
    string BackgroundColor,
    string AccentColor,
    string DetailMode,
    string? Eyebrow = null,
    string? ImageUrl = null,
    string? ExternalUrl = null,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null,
    int SortOrder = 0,
    Guid[]? LocationIds = null,
    Guid[]? ProductIds = null
);

/// <summary>Full-replace PUT body — mirrors every field <c>Banner.Update</c> accepts, plus the join arrays.</summary>
public sealed record UpdateBannerRequest(
    string Title,
    string Description,
    string Body,
    string Terms,
    string Icon,
    string BackgroundColor,
    string AccentColor,
    string DetailMode,
    DateTime ValidFrom,
    string? Eyebrow = null,
    string? ImageUrl = null,
    string? ExternalUrl = null,
    DateTime? ValidUntil = null,
    int SortOrder = 0,
    Guid[]? LocationIds = null,
    Guid[]? ProductIds = null
);

public sealed record BannerListQuery(
    Guid? LocationId = null,
    bool? ActiveOnly = null
);

public sealed record BannerAnalyticsDto(
    int ViewCount,
    int ClickCount,
    decimal Ctr
);
