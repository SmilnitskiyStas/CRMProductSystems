namespace ShelfGuard.Application.Features.PromotionCampaigns.Dtos;

public sealed record PromotionCampaignProductRequest(Guid ProductId, decimal DiscountPercent);

public sealed record UpsertPromotionCampaignRequest(
    string Title, string? Eyebrow, string Description, string Body, string Terms,
    string BackgroundColor, string AccentColor, string AudienceType, Guid[] AudienceTierIds,
    DateTime StartsAt, DateTime? EndsAt, int SortOrder, Guid[] LocationIds,
    PromotionCampaignProductRequest[] Products, bool PublishImmediately = false);

public sealed record PromotionCampaignProductDto(Guid ProductId, string? ProductName, string? ImageUrl, decimal? PriceRetail, decimal DiscountPercent);

public sealed record PromotionCampaignDto(
    Guid Id, string Title, string? Eyebrow, string Description, string Body, string Terms,
    string? ImageUrl, string BackgroundColor, string AccentColor, string AudienceType,
    Guid[] AudienceTierIds, DateTime StartsAt, DateTime? EndsAt, string Status, int SortOrder,
    Guid[] LocationIds, PromotionCampaignProductDto[] Products, DateTime CreatedAt, DateTime UpdatedAt,
    DateTime? PublishedAt);
