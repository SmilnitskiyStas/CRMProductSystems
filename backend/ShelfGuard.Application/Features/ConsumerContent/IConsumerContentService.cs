using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.ConsumerContent.Dtos;

namespace ShelfGuard.Application.Features.ConsumerContent;

public interface IConsumerContentService
{
    Task<(IReadOnlyList<ConsumerBannerDto>? Banners, string? Error)> GetActiveBannersAsync(
        Guid tenantId, Guid storeId, CancellationToken ct = default);

    Task<(bool Success, string? Error)> RecordBannerEventAsync(
        Guid tenantId, Guid bannerId, Guid storeId, string eventType, Guid? consumerAccountId, CancellationToken ct = default);

    Task<(IReadOnlyList<ConsumerPromotionDto>? Promotions, string? Error)> GetActivePromotionsAsync(
        Guid tenantId, Guid storeId, CancellationToken ct = default);
    Task<(IReadOnlyList<ConsumerPromotionCampaignDto>? Campaigns, string? Error)> GetActivePromotionCampaignsAsync(
        Guid tenantId, Guid storeId, Guid? consumerAccountId, CancellationToken ct = default);
    Task<(bool Success, string? Error)> RecordPromotionCampaignEventAsync(
        Guid tenantId, Guid campaignId, Guid storeId, string eventType, Guid? consumerAccountId,
        CancellationToken ct = default);

    Task<(PagedResult<ConsumerCatalogItemDto>? Catalog, string? Error)> GetCatalogAsync(
        Guid tenantId, Guid storeId, string? search, Guid? categoryId, int page, int pageSize,
        CancellationToken ct = default);

    /// <summary>Active catalog items matching exactly the given ids — resolves a curated productIds
    /// selection regardless of alphabetical position (TASK-570/572, ADR-032).</summary>
    Task<(IReadOnlyList<ConsumerCatalogItemDto>? Items, string? Error)> GetCatalogByIdsAsync(
        Guid tenantId, Guid storeId, IReadOnlyList<Guid> ids, CancellationToken ct = default);
}
