using ShelfGuard.Application.Features.ConsumerContent.Dtos;

namespace ShelfGuard.Application.Features.ConsumerContent;

/// <summary>
/// Read-only cross-feature queries backing the public consumer content endpoints (banners,
/// promotions, catalog browse — TASK-521). Same shape/precedent as
/// Features.Analytics.IAnalyticsRepository: a repository interface that returns Application
/// DTOs directly rather than domain entities, because these queries join across
/// Banner/BannerLocation/BannerProduct/Discount/Item/ProductStock — no single feature "owns"
/// this read shape.
///
/// Every method here is expected to run inside an ITenantSessionOverride.ExecuteAsync block
/// (see ConsumerContentService) — the caller may be a fully anonymous consumer-app session with
/// no app.tenant_id claim at all, so plain tenant_isolation RLS would otherwise hide every row.
/// </summary>
public interface IConsumerContentRepository
{
    /// <summary>Active banners assigned to <paramref name="storeId"/>, ordered by SortOrder, with attached products.</summary>
    Task<IReadOnlyList<ConsumerBannerDto>> GetActiveBannersAsync(
        Guid tenantId, Guid storeId, DateTime utcNow, CancellationToken ct = default);

    Task<bool> BannerExistsAsync(Guid tenantId, Guid bannerId, CancellationToken ct = default);

    /// <summary>Inserts one BannerEvent row and saves. consumerAccountId is null for anonymous sessions.</summary>
    Task RecordEventAsync(
        Guid tenantId, Guid bannerId, string eventType, Guid? consumerAccountId, CancellationToken ct = default);

    /// <summary>Active Discount rows (Status=active, within ValidFrom/ValidUntil) for one store, joined with Item.</summary>
    Task<IReadOnlyList<ConsumerPromotionDto>> GetActivePromotionsAsync(
        Guid tenantId, Guid storeId, DateTime utcNow, CancellationToken ct = default);

    /// <summary>Paginated active Items for the tenant, annotated with availability at one store.</summary>
    Task<(IReadOnlyList<ConsumerCatalogItemDto> Items, int Total)> GetCatalogPagedAsync(
        Guid tenantId, Guid storeId, string? search, Guid? categoryId, int page, int pageSize,
        CancellationToken ct = default);
}
