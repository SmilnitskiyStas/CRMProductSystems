using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.ConsumerContent.Dtos;

namespace ShelfGuard.Application.Features.ConsumerContent;

public interface IConsumerContentService
{
    Task<(IReadOnlyList<ConsumerBannerDto>? Banners, string? Error)> GetActiveBannersAsync(
        Guid tenantId, Guid storeId, CancellationToken ct = default);

    Task<(bool Success, string? Error)> RecordBannerEventAsync(
        Guid tenantId, Guid bannerId, string eventType, Guid? consumerAccountId, CancellationToken ct = default);

    Task<(IReadOnlyList<ConsumerPromotionDto>? Promotions, string? Error)> GetActivePromotionsAsync(
        Guid tenantId, Guid storeId, CancellationToken ct = default);

    Task<(PagedResult<ConsumerCatalogItemDto>? Catalog, string? Error)> GetCatalogAsync(
        Guid tenantId, Guid storeId, string? search, Guid? categoryId, int page, int pageSize,
        CancellationToken ct = default);
}
