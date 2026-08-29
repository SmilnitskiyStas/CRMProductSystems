using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.ConsumerContent;
using ShelfGuard.Application.Features.ConsumerContent.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Public consumer-app marketing content (Consumer App plan, TASK-521): active banners,
/// active promotions (a read projection over Discount — DiscountsController/IDiscountService
/// are unchanged), and a paginated catalog browse. All endpoints work with NO Authorization
/// header (anonymous marketing-content browsing — same [AllowAnonymous] posture as
/// MarketplaceController's public listings). When the caller DOES send a consumer JWT
/// (ConsumerLoyaltyController's "consumer_account_id" claim), view/click events are attributed
/// to that account; otherwise they are recorded anonymously.
///
/// tenantId is a route parameter, not a JWT claim — a consumer/anonymous session never carries
/// app.tenant_id (see TenantConnectionInterceptor remarks), so every read/write here runs
/// through ITenantSessionOverride inside ConsumerContentService, the same mechanism
/// ConsumerLoyaltyController/LoyaltyService already use for cross-tenant consumer sessions.
///
/// TASK-558: GetPromotions/GetCatalog are gated by [RequireConsumerFeature("promotions"/"catalog")]
/// — see RequireConsumerFeatureAttribute/IConsumerFeatureFlagService remarks for the
/// default-enabled production-safety contract this relies on. GetBanners/RecordView/RecordClick
/// stay ungated: "banners" is not one of MobileConfigWhitelists.FeatureKeys' 8 flags, so there is
/// no real flag to map it to.
/// </summary>
[ApiController]
[Route("api/consumer")]
[AllowAnonymous]
public sealed class ConsumerContentController : ControllerBase
{
    private readonly IConsumerContentService _service;

    public ConsumerContentController(IConsumerContentService service) => _service = service;

    /// <summary>Active banners assigned to storeId, ordered for display, with attached products.</summary>
    [HttpGet("{tenantId:guid}/banners")]
    public async Task<IActionResult> GetBanners(Guid tenantId, [FromQuery] Guid storeId, CancellationToken ct)
    {
        var (banners, error) = await _service.GetActiveBannersAsync(tenantId, storeId, ct);
        return banners is null ? NotFound(new { error }) : Ok(banners);
    }

    /// <summary>Records a banner impression. consumerAccountId is attached when a consumer JWT is present.</summary>
    [HttpPost("{tenantId:guid}/banners/{id:guid}/view")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> RecordView(Guid tenantId, Guid id, [FromQuery] Guid storeId, CancellationToken ct) =>
        RecordEvent(tenantId, id, storeId, BannerEventType.View, ct);

    /// <summary>Records a banner tap-through. consumerAccountId is attached when a consumer JWT is present.</summary>
    [HttpPost("{tenantId:guid}/banners/{id:guid}/click")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> RecordClick(Guid tenantId, Guid id, [FromQuery] Guid storeId, CancellationToken ct) =>
        RecordEvent(tenantId, id, storeId, BannerEventType.Click, ct);

    /// <summary>Active discounted products for a store — read projection over Discount, no Discount API changes.</summary>
    [HttpGet("{tenantId:guid}/promotions")]
    [RequireConsumerFeature("promotions")]
    public async Task<IActionResult> GetPromotions(Guid tenantId, [FromQuery] Guid storeId, CancellationToken ct)
    {
        var (promotions, error) = await _service.GetActivePromotionsAsync(tenantId, storeId, ct);
        return promotions is null ? NotFound(new { error }) : Ok(promotions);
    }

    [HttpGet("{tenantId:guid}/promotion-campaigns")]
    [RequireConsumerFeature("promotions")]
    public async Task<IActionResult> GetPromotionCampaigns(Guid tenantId, [FromQuery] Guid storeId, CancellationToken ct)
    {
        var (campaigns, error) = await _service.GetActivePromotionCampaignsAsync(tenantId, storeId, ResolveConsumerAccountId(), ct);
        return campaigns is null ? NotFound(new { error }) : Ok(campaigns);
    }

    [HttpPost("{tenantId:guid}/promotion-campaigns/{id:guid}/{eventType}")]
    [RequireConsumerFeature("promotions")]
    public async Task<IActionResult> RecordPromotionCampaignEvent(
        Guid tenantId, Guid id, string eventType, [FromQuery] Guid storeId, CancellationToken ct)
    {
        var (success, error) = await _service.RecordPromotionCampaignEventAsync(
            tenantId, id, storeId, eventType, ResolveConsumerAccountId(), ct);
        return success ? NoContent() : NotFound(new { error });
    }

    /// <summary>Paginated active catalog for the tenant, annotated with availability at storeId.</summary>
    [HttpGet("{tenantId:guid}/catalog")]
    [RequireConsumerFeature("catalog")]
    public async Task<IActionResult> GetCatalog(
        Guid tenantId,
        [FromQuery] Guid storeId,
        [FromQuery] string? search = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var (catalog, error) = await _service.GetCatalogAsync(tenantId, storeId, search, categoryId, page, pageSize, ct);
        return catalog is null ? NotFound(new { error }) : Ok(catalog);
    }

    /// <summary>Active catalog items matching exactly the given ids — resolves a curated productIds
    /// selection regardless of alphabetical position (TASK-570/572, ADR-032).</summary>
    [HttpGet("{tenantId:guid}/catalog/by-ids")]
    [RequireConsumerFeature("catalog")]
    public async Task<IActionResult> GetCatalogByIds(
        Guid tenantId, [FromQuery] Guid storeId, [FromQuery(Name = "ids")] Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return Ok(Array.Empty<ConsumerCatalogItemDto>());

        var (items, error) = await _service.GetCatalogByIdsAsync(tenantId, storeId, ids.Take(30).ToList(), ct);
        return items is null ? NotFound(new { error }) : Ok(items);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<IActionResult> RecordEvent(Guid tenantId, Guid bannerId, Guid storeId, string eventType, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        var (success, error) = await _service.RecordBannerEventAsync(tenantId, bannerId, storeId, eventType, consumerId, ct);
        return success ? NoContent() : NotFound(new { error });
    }

    /// <summary>
    /// Optional — unlike ConsumerLoyaltyController.ResolveConsumerAccountId, a missing/absent
    /// claim here is not a 403: anonymous view/click tracking is an explicit requirement (public
    /// marketing content), so this simply returns null and the event is recorded with no
    /// ConsumerAccountId attached.
    /// </summary>
    private Guid? ResolveConsumerAccountId()
    {
        var claim = User.FindFirst("consumer_account_id")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
