using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.ConsumerContent.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.ConsumerContent;

/// <summary>
/// Public, tenant-scoped read surface for the consumer app's home feed (banners), active
/// promotions (Discount read projection), and catalog browse (TASK-521). Every data access
/// runs through ITenantSessionOverride — the caller may be a fully anonymous request or a
/// ConsumerAccount session, neither of which carries an app.tenant_id claim (see
/// TenantConnectionInterceptor remarks), so plain tenant_isolation RLS would otherwise hide
/// every row. tenantId comes from the route (already an explicit, business-meaningful
/// parameter — "browse this tenant's public marketing content" — matching the security
/// contract ITenantSessionOverride documents), the same pattern ConsumerLoyaltyController /
/// LoyaltyService already use.
///
/// Tenants carries no RLS at all (see LoyaltyService.JoinAsync precedent), so the existence
/// check below runs before any override is needed.
/// </summary>
public sealed class ConsumerContentService : IConsumerContentService
{
    private readonly IConsumerContentRepository _repo;
    private readonly ITenantRepository _tenants;
    private readonly ITenantSessionOverride _tenantScope;

    public ConsumerContentService(
        IConsumerContentRepository repo, ITenantRepository tenants, ITenantSessionOverride tenantScope)
    {
        _repo = repo;
        _tenants = tenants;
        _tenantScope = tenantScope;
    }

    public async Task<(IReadOnlyList<ConsumerBannerDto>? Banners, string? Error)> GetActiveBannersAsync(
        Guid tenantId, Guid storeId, CancellationToken ct = default)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null) return (null, "Tenant not found.");

        var banners = await _tenantScope.ExecuteAsync(
            tenantId, () => _repo.GetActiveBannersAsync(tenantId, storeId, DateTime.UtcNow, ct), ct);

        return (banners, null);
    }

    public async Task<(bool Success, string? Error)> RecordBannerEventAsync(
        Guid tenantId, Guid bannerId, Guid storeId, string eventType, Guid? consumerAccountId, CancellationToken ct = default)
    {
        if (eventType is not (BannerEventType.View or BannerEventType.Click))
            return (false, $"Invalid eventType '{eventType}'.");

        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null) return (false, "Tenant not found.");

        // Existence check + insert share one override transaction — avoids a second SET
        // LOCAL/BEGIN round trip for what is otherwise a single-row write.
        var recorded = await _tenantScope.ExecuteAsync(tenantId, async () =>
        {
            if (!await _repo.BannerExistsAsync(tenantId, bannerId, ct))
                return false;

            await _repo.RecordEventAsync(tenantId, bannerId, storeId, eventType, consumerAccountId, ct);
            return true;
        }, ct);

        return recorded ? (true, null) : (false, "Banner not found.");
    }

    public async Task<(IReadOnlyList<ConsumerPromotionDto>? Promotions, string? Error)> GetActivePromotionsAsync(
        Guid tenantId, Guid storeId, CancellationToken ct = default)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null) return (null, "Tenant not found.");

        var promotions = await _tenantScope.ExecuteAsync(
            tenantId, () => _repo.GetActivePromotionsAsync(tenantId, storeId, DateTime.UtcNow, ct), ct);

        return (promotions, null);
    }

    public async Task<(IReadOnlyList<ConsumerPromotionCampaignDto>? Campaigns, string? Error)> GetActivePromotionCampaignsAsync(Guid tenantId, Guid storeId, Guid? consumerAccountId, CancellationToken ct = default)
    {
        if (await _tenants.GetByIdAsync(tenantId, ct) is null) return (null, "Tenant not found.");
        var result = await _tenantScope.ExecuteAsync(tenantId, () => _repo.GetActivePromotionCampaignsAsync(tenantId, storeId, consumerAccountId, DateTime.UtcNow, ct), ct);
        return (result, null);
    }

    public async Task<(bool Success, string? Error)> RecordPromotionCampaignEventAsync(
        Guid tenantId, Guid campaignId, Guid storeId, string eventType, Guid? consumerAccountId,
        CancellationToken ct = default)
    {
        if (!PromotionCampaignEventType.IsValid(eventType)) return (false, "Invalid promotion campaign event type.");
        if (await _tenants.GetByIdAsync(tenantId, ct) is null) return (false, "Tenant not found.");
        var recorded = await _tenantScope.ExecuteAsync(tenantId, async () =>
        {
            if (!await _repo.PromotionCampaignExistsAtStoreAsync(tenantId, campaignId, storeId, ct)) return false;
            await _repo.RecordPromotionCampaignEventAsync(tenantId, campaignId, storeId, eventType, consumerAccountId, ct);
            return true;
        }, ct);
        return recorded ? (true, null) : (false, "Promotion campaign not found for this store.");
    }

    public async Task<(PagedResult<ConsumerCatalogItemDto>? Catalog, string? Error)> GetCatalogAsync(
        Guid tenantId, Guid storeId, string? search, Guid? categoryId, int page, int pageSize,
        CancellationToken ct = default)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null) return (null, "Tenant not found.");

        var (items, total) = await _tenantScope.ExecuteAsync(
            tenantId,
            () => _repo.GetCatalogPagedAsync(tenantId, storeId, search, categoryId, page, pageSize, ct),
            ct);

        return (new PagedResult<ConsumerCatalogItemDto>
        {
            Items = items.ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        }, null);
    }

    public async Task<(IReadOnlyList<ConsumerCatalogItemDto>? Items, string? Error)> GetCatalogByIdsAsync(
        Guid tenantId, Guid storeId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null) return (null, "Tenant not found.");

        if (ids.Count == 0) return (Array.Empty<ConsumerCatalogItemDto>(), null);

        // Defensive clamp — the controller already bounds to 30, but the registry's own MaxItems
        // (BlockRegistry.cs productGrid/productCarousel) is the real source of truth for "why 30".
        var clampedIds = ids.Count > 30 ? ids.Take(30).ToList() : ids;

        var items = await _tenantScope.ExecuteAsync(
            tenantId, () => _repo.GetCatalogByIdsAsync(tenantId, storeId, clampedIds, ct), ct);

        return (items, null);
    }
}
