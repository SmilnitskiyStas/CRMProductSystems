using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.PromotionCampaigns;
using ShelfGuard.Application.Features.PromotionCampaigns.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Application.Features.ConsumerAnalytics;
using System.Text.Json;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
[Route("api/promotion-campaigns")]
[RequireModule("mobile_app")] // TASK-674: "Застосунок" admin section
public sealed class PromotionCampaignsController : ControllerBase
{
    private readonly IPromotionCampaignService _service;
    private readonly ITenantContext _tenant;
    private readonly AppDbContext _db;
    public PromotionCampaignsController(IPromotionCampaignService service, ITenantContext tenant, AppDbContext db) { _service = service; _tenant = tenant; _db = db; }

    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await _service.GetAllAsync(_tenant.TenantId!.Value, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) { var (x,e)=await _service.GetByIdAsync(_tenant.TenantId!.Value,id,ct); return x is null?NotFound(new{error=e}):Ok(x); }
    [HttpPost] public async Task<IActionResult> Create([FromBody] UpsertPromotionCampaignRequest request, CancellationToken ct) { var (x,e)=await _service.CreateAsync(_tenant.TenantId!.Value,UserId(),request,ct); return x is null?BadRequest(new{error=e}):CreatedAtAction(nameof(Get),new{id=x.Id},x); }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id,[FromBody] UpsertPromotionCampaignRequest request,CancellationToken ct) { var(x,e)=await _service.UpdateAsync(_tenant.TenantId!.Value,id,UserId(),request,ct); return x is null?BadRequest(new{error=e}):Ok(x); }
    [HttpPost("{id:guid}/publish")] public async Task<IActionResult> Publish(Guid id,CancellationToken ct) { var(x,e)=await _service.PublishAsync(_tenant.TenantId!.Value,id,UserId(),ct); return x is null?BadRequest(new{error=e}):Ok(x); }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Cancel(Guid id,CancellationToken ct) { var(ok,e)=await _service.CancelAsync(_tenant.TenantId!.Value,id,ct); return ok?NoContent():NotFound(new{error=e}); }
    [HttpGet("{id:guid}/analytics")]
    public async Task<IActionResult> Analytics(Guid id, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] Guid[]? storeIds = null, CancellationToken ct = default)
    {
        var tenantId = _tenant.TenantId!.Value;
        var campaign = await _db.PromotionCampaigns.AsNoTracking().Include(x => x.Products).Include(x => x.Locations)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (campaign is null) return NotFound();
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date) return BadRequest(new { error = "Invalid date range." });
        var start = from?.Date ?? campaign.StartsAt;
        if (start < campaign.StartsAt) start = campaign.StartsAt;
        var endExclusive = to?.Date.AddDays(1) ?? campaign.EndsAt?.AddTicks(1) ?? DateTime.UtcNow.AddTicks(1);
        if (campaign.EndsAt.HasValue && endExclusive > campaign.EndsAt.Value.AddTicks(1)) endExclusive = campaign.EndsAt.Value.AddTicks(1);
        var campaignStores = campaign.Locations.Select(x => x.LocationId).Distinct().ToArray();
        var requestedStores = storeIds?.Distinct().ToArray() ?? [];
        var stores = requestedStores.Length == 0
            ? campaignStores
            : requestedStores.Intersect(campaignStores).ToArray();
        var eventsQuery = _db.PromotionCampaignEvents.AsNoTracking().Where(x => x.TenantId == tenantId && x.CampaignId == id && x.OccurredAt >= start && x.OccurredAt < endExclusive);
        eventsQuery = eventsQuery.Where(x => stores.Contains(x.StoreId));
        var events = await eventsQuery.ToListAsync(ct);
        var productIds = campaign.Products.Select(x => x.ProductId).ToArray();
        var txQuery = _db.PosTransactions.AsNoTracking().Include(x => x.Items).Include(x => x.LoyaltyMembership).Where(x => x.TenantId == tenantId
            && x.LoyaltyMembershipId.HasValue && x.Status != "cancelled" && x.CreatedAt >= start && x.CreatedAt < endExclusive);
        txQuery = txQuery.Where(x => stores.Contains(x.StoreId));
        var transactions = await txQuery.ToListAsync(ct);
        if (campaign.AudienceType == PromotionAudienceType.LoyaltyTiers)
        {
            var allowedTiers = JsonSerializer.Deserialize<Guid[]>(campaign.AudienceTierIdsJson) ?? [];
            transactions = transactions.Where(x => x.LoyaltyMembership?.CurrentTierId is Guid tierId && allowedTiers.Contains(tierId)).ToList();
        }
        var used = transactions.Select(tx => new { Tx = tx, Lines = tx.Items.Where(line => productIds.Contains(line.ProductId)).ToList() }).Where(x => x.Lines.Count > 0).ToList();
        var impressions = events.Count(x => x.EventType == PromotionCampaignEventType.Impression);
        var opens = events.Count(x => x.EventType == PromotionCampaignEventType.Open);
        var purchases = used.Sum(x => x.Lines.Sum(line => line.Quantity));
        var revenue = used.Sum(x => x.Lines.Sum(line => line.PriceFinal * line.Quantity));
        var dates = events.Select(x => x.OccurredAt.Date).Concat(used.Select(x => x.Tx.CreatedAt.Date)).Distinct().OrderBy(x => x).ToList();
        var daily = dates.Select(date => new PromotionCampaignDailyAnalyticsDto(date,
            events.Count(x => x.OccurredAt.Date == date && x.EventType == PromotionCampaignEventType.Impression),
            events.Count(x => x.OccurredAt.Date == date && x.EventType == PromotionCampaignEventType.Open),
            used.Count(x => x.Tx.CreatedAt.Date == date),
            used.Where(x => x.Tx.CreatedAt.Date == date).Sum(x => x.Lines.Sum(line => line.PriceFinal * line.Quantity)))).ToList();
        var storeNames = await _db.Locations.AsNoTracking().Where(x => stores.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var storeRows = stores.Select(storeId => new PromotionCampaignStoreAnalyticsDto(storeId, storeNames.GetValueOrDefault(storeId, "Магазин"),
            events.Count(x => x.StoreId == storeId && x.EventType == PromotionCampaignEventType.Impression),
            events.Count(x => x.StoreId == storeId && x.EventType == PromotionCampaignEventType.Open),
            used.Count(x => x.Tx.StoreId == storeId),
            used.Where(x => x.Tx.StoreId == storeId).Sum(x => x.Lines.Sum(line => line.PriceFinal * line.Quantity)))).OrderByDescending(x => x.Revenue).ToList();
        var productNames = await _db.Items.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var productRows = campaign.Products.Select(product => new PromotionCampaignProductAnalyticsDto(product.ProductId, productNames.GetValueOrDefault(product.ProductId, "Товар"),
            used.Sum(x => x.Lines.Where(line => line.ProductId == product.ProductId).Sum(line => line.Quantity)),
            used.Sum(x => x.Lines.Where(line => line.ProductId == product.ProductId).Sum(line => line.PriceFinal * line.Quantity)))).OrderByDescending(x => x.Revenue).ToList();
        var consumerIds = events.Where(x => x.ConsumerAccountId.HasValue).Select(x => x.ConsumerAccountId!.Value).Distinct().ToArray();
        var eventMemberships = await _db.LoyaltyMemberships.AsNoTracking().Where(x => x.TenantId == tenantId && consumerIds.Contains(x.ConsumerAccountId)).ToListAsync(ct);
        var memberships = eventMemberships.Concat(transactions.Where(x => x.LoyaltyMembership is not null).Select(x => x.LoyaltyMembership!)).DistinctBy(x => x.Id).ToList();
        var membershipByConsumer = memberships.ToDictionary(x => x.ConsumerAccountId);
        var membershipIds = memberships.Select(x => x.Id).Distinct().ToArray();
        var firstPurchases = membershipIds.Length == 0 ? new Dictionary<Guid, DateTime>() : await _db.PosTransactions.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LoyaltyMembershipId.HasValue && membershipIds.Contains(x.LoyaltyMembershipId.Value) && x.Status != "cancelled")
            .GroupBy(x => x.LoyaltyMembershipId!.Value).Select(g => new { MembershipId = g.Key, FirstAt = g.Min(x => x.CreatedAt) }).ToDictionaryAsync(x => x.MembershipId, x => x.FirstAt, ct);
        var tierIds = memberships.Where(x => x.CurrentTierId.HasValue).Select(x => x.CurrentTierId!.Value).Distinct().ToArray();
        var tierNames = await _db.LoyaltyTierDefinitions.AsNoTracking().Where(x => x.TenantId == tenantId && tierIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        PromotionCampaignAudienceAnalyticsDto Audience(string key, string label, Guid? tierId, Func<LoyaltyMembership, bool> memberMatch)
        {
            var segmentEvents = events.Where(x => x.ConsumerAccountId is Guid consumerId && membershipByConsumer.TryGetValue(consumerId, out var membership) && memberMatch(membership)).ToList();
            var segmentUsed = used.Where(x => x.Tx.LoyaltyMembership is not null && memberMatch(x.Tx.LoyaltyMembership)).ToList();
            return new PromotionCampaignAudienceAnalyticsDto(key, label, tierId,
                segmentEvents.Count(x => x.EventType == PromotionCampaignEventType.Impression),
                segmentEvents.Count(x => x.EventType == PromotionCampaignEventType.Open),
                segmentUsed.Count,
                segmentUsed.Sum(x => x.Lines.Sum(line => line.PriceFinal * line.Quantity)));
        }
        var audience = new List<PromotionCampaignAudienceAnalyticsDto>
        {
            new("all", "Усі клієнти", null, impressions, opens, used.Count, revenue),
            Audience("loyalty", "Учасники програми лояльності", null, _ => true),
            Audience("new", "Нові покупці", null, membership => firstPurchases.TryGetValue(membership.Id, out var firstAt) && firstAt >= start),
            Audience("returning", "Постійні покупці", null, membership => firstPurchases.TryGetValue(membership.Id, out var firstAt) && firstAt < start),
        };
        audience.AddRange(tierIds.Select(tierId => Audience($"tier:{tierId}", tierNames.GetValueOrDefault(tierId, "Рівень лояльності"), tierId, membership => membership.CurrentTierId == tierId)));
        return Ok(new PromotionCampaignAnalyticsDto(id, impressions, opens, events.Where(x => x.ConsumerAccountId.HasValue).Select(x => x.ConsumerAccountId).Distinct().Count(),
            used.Count, purchases, revenue, impressions == 0 ? 0 : Math.Round((decimal)opens / impressions * 100, 2), opens == 0 ? 0 : Math.Round((decimal)used.Count / opens * 100, 2), daily, storeRows, productRows, audience, ConsumerOfferAttributionPolicy.Describe()));
    }
    [HttpPost("{id:guid}/image")]
    public async Task<IActionResult> Upload(Guid id,IFormFile file,CancellationToken ct)
    {
        if (file.Length is <=0 or >5*1024*1024) return BadRequest(new{error="Image must be between 1 byte and 5 MB."});
        var ext=Path.GetExtension(file.FileName).ToLowerInvariant(); if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) return BadRequest(new{error="Unsupported image format."});
        await using var stream=file.OpenReadStream(); var(url,e)=await _service.UploadImageAsync(_tenant.TenantId!.Value,id,stream,ext,ct); return url is null?BadRequest(new{error=e}):Ok(new{imageUrl=url});
    }
    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:Guid.Empty;
}

public sealed record PromotionCampaignAnalyticsDto(Guid CampaignId, int Impressions, int Opens, int UniqueUsers,
    int UsedReceipts, decimal PurchasedUnits, decimal Revenue, decimal OpenRatePercent, decimal ConversionPercent,
    IReadOnlyList<PromotionCampaignDailyAnalyticsDto> Daily, IReadOnlyList<PromotionCampaignStoreAnalyticsDto> Stores,
    IReadOnlyList<PromotionCampaignProductAnalyticsDto> Products, IReadOnlyList<PromotionCampaignAudienceAnalyticsDto> Audience, ConsumerOfferAttributionPolicyDto AttributionPolicy);
public sealed record PromotionCampaignDailyAnalyticsDto(DateTime Date, int Impressions, int Opens, int UsedReceipts, decimal Revenue);
public sealed record PromotionCampaignStoreAnalyticsDto(Guid StoreId, string StoreName, int Impressions, int Opens, int UsedReceipts, decimal Revenue);
public sealed record PromotionCampaignProductAnalyticsDto(Guid ProductId, string ProductName, decimal PurchasedUnits, decimal Revenue);
public sealed record PromotionCampaignAudienceAnalyticsDto(string Key, string Label, Guid? TierId, int Reach, int Interactions, int Purchases, decimal Revenue);
