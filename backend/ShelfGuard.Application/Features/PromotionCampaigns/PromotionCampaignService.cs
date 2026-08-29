using System.Text.Json;
using ShelfGuard.Application.Features.PromotionCampaigns.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.PromotionCampaigns;

public sealed class PromotionCampaignService : IPromotionCampaignService
{
    private readonly IPromotionCampaignRepository _campaigns;
    private readonly IDiscountRepository _discounts;
    public PromotionCampaignService(IPromotionCampaignRepository campaigns, IDiscountRepository discounts) { _campaigns = campaigns; _discounts = discounts; }

    public async Task<IReadOnlyList<PromotionCampaignDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default) =>
        (await _campaigns.GetAllAsync(tenantId, ct)).Select(ToDto).ToList();

    public async Task<(PromotionCampaignDto? Campaign, string? Error)> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var entity = await _campaigns.GetByIdAsync(tenantId, id, ct);
        return entity is null ? (null, "Promotion campaign not found.") : (ToDto(entity), null);
    }

    public async Task<(PromotionCampaignDto? Campaign, string? Error)> CreateAsync(Guid tenantId, Guid userId, UpsertPromotionCampaignRequest request, CancellationToken ct = default)
    {
        var error = Validate(request); if (error is not null) return (null, error);
        var entity = new PromotionCampaign { TenantId = tenantId, CreatedBy = userId, StartsAt = request.StartsAt };
        Apply(entity, request);
        await _campaigns.AddAsync(entity, ct);
        await _campaigns.ReplaceLocationsAsync(tenantId, entity.Id, request.LocationIds, ct);
        await _campaigns.ReplaceProductsAsync(tenantId, entity.Id, request.Products.Select(x => (x.ProductId, x.DiscountPercent)).ToList(), ct);
        await _campaigns.SaveChangesAsync(ct);
        if (request.PublishImmediately) return await PublishAsync(tenantId, entity.Id, userId, ct);
        return await GetByIdAsync(tenantId, entity.Id, ct);
    }

    public async Task<(PromotionCampaignDto? Campaign, string? Error)> UpdateAsync(Guid tenantId, Guid id, Guid userId, UpsertPromotionCampaignRequest request, CancellationToken ct = default)
    {
        var error = Validate(request); if (error is not null) return (null, error);
        var entity = await _campaigns.GetByIdAsync(tenantId, id, ct);
        if (entity is null) return (null, "Promotion campaign not found.");
        if (entity.Status == PromotionCampaignStatus.Published) return (null, "Published campaign cannot be edited; cancel it first.");
        Apply(entity, request);
        await _campaigns.ReplaceLocationsAsync(tenantId, id, request.LocationIds, ct);
        await _campaigns.ReplaceProductsAsync(tenantId, id, request.Products.Select(x => (x.ProductId, x.DiscountPercent)).ToList(), ct);
        await _campaigns.SaveChangesAsync(ct);
        if (request.PublishImmediately) return await PublishAsync(tenantId, id, userId, ct);
        return await GetByIdAsync(tenantId, id, ct);
    }

    public async Task<(PromotionCampaignDto? Campaign, string? Error)> PublishAsync(Guid tenantId, Guid id, Guid userId, CancellationToken ct = default)
    {
        var entity = await _campaigns.GetByIdAsync(tenantId, id, ct);
        if (entity is null) return (null, "Promotion campaign not found.");
        if (entity.Products.Count == 0 || entity.Locations.Count == 0) return (null, "Campaign requires at least one product and store.");
        if (entity.Status == PromotionCampaignStatus.Published) return (ToDto(entity), null);
        foreach (var location in entity.Locations)
        foreach (var product in entity.Products)
        {
            var discount = Discount.Create(tenantId, product.ProductId, location.LocationId, product.DiscountPercent,
                DiscountReason.Promo, priceOriginal: product.Product?.PriceRetail, validFrom: entity.StartsAt,
                validUntil: entity.EndsAt, createdBy: userId, promotionCampaignId: entity.Id);
            discount.Approve(userId);
            await _discounts.AddAsync(discount, ct);
        }
        entity.Status = PromotionCampaignStatus.Published; entity.PublishedAt = DateTime.UtcNow; entity.UpdatedAt = DateTime.UtcNow;
        await _campaigns.SaveChangesAsync(ct);
        return await GetByIdAsync(tenantId, id, ct);
    }

    public async Task<(bool Success, string? Error)> CancelAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var entity = await _campaigns.GetByIdAsync(tenantId, id, ct);
        if (entity is null) return (false, "Promotion campaign not found.");
        foreach (var discount in await _campaigns.GetCampaignDiscountsAsync(tenantId, id, ct))
            if (discount.Status is DiscountStatus.Pending or DiscountStatus.Active) { discount.Cancel(); _discounts.Update(discount); }
        entity.Status = PromotionCampaignStatus.Cancelled; entity.UpdatedAt = DateTime.UtcNow;
        await _campaigns.SaveChangesAsync(ct); return (true, null);
    }

    public async Task<(string? Url, string? Error)> UploadImageAsync(Guid tenantId, Guid id, Stream stream, string extension, CancellationToken ct = default)
    {
        var entity = await _campaigns.GetByIdAsync(tenantId, id, ct); if (entity is null) return (null, "Promotion campaign not found.");
        var dir = Path.Combine("wwwroot", "uploads", "promotion-campaigns"); Directory.CreateDirectory(dir);
        var name = $"{id:N}-{Guid.NewGuid():N}{extension}"; await using var output = File.Create(Path.Combine(dir, name)); await stream.CopyToAsync(output, ct);
        entity.ImageUrl = $"/uploads/promotion-campaigns/{name}"; entity.UpdatedAt = DateTime.UtcNow; await _campaigns.SaveChangesAsync(ct);
        return (entity.ImageUrl, null);
    }

    private static string? Validate(UpsertPromotionCampaignRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Title) || string.IsNullOrWhiteSpace(r.Description)) return "Title and description are required.";
        if (r.EndsAt.HasValue && r.EndsAt <= r.StartsAt) return "EndsAt must be after StartsAt.";
        if (!PromotionAudienceType.AllValues.Contains(r.AudienceType)) return "Invalid audience type.";
        if (r.AudienceType == PromotionAudienceType.LoyaltyTiers && r.AudienceTierIds.Length == 0) return "Select at least one loyalty tier.";
        if (r.LocationIds.Length == 0 || r.Products.Length == 0) return "Select at least one store and product.";
        if (r.Products.Any(x => x.DiscountPercent is <= 0 or > 100)) return "Discount percent must be between 0.01 and 100.";
        return null;
    }
    private static void Apply(PromotionCampaign e, UpsertPromotionCampaignRequest r) { e.Title=r.Title.Trim(); e.Eyebrow=r.Eyebrow?.Trim(); e.Description=r.Description.Trim(); e.Body=r.Body; e.Terms=r.Terms; e.BackgroundColor=r.BackgroundColor; e.AccentColor=r.AccentColor; e.AudienceType=r.AudienceType; e.AudienceTierIdsJson=JsonSerializer.Serialize(r.AudienceTierIds.Distinct()); e.StartsAt=r.StartsAt; e.EndsAt=r.EndsAt; e.SortOrder=r.SortOrder; e.UpdatedAt=DateTime.UtcNow; }
    private static PromotionCampaignDto ToDto(PromotionCampaign e) => new(e.Id,e.Title,e.Eyebrow,e.Description,e.Body,e.Terms,e.ImageUrl,e.BackgroundColor,e.AccentColor,e.AudienceType,JsonSerializer.Deserialize<Guid[]>(e.AudienceTierIdsJson)??[],e.StartsAt,e.EndsAt,e.Status,e.SortOrder,e.Locations.Select(x=>x.LocationId).ToArray(),e.Products.Select(x=>new PromotionCampaignProductDto(x.ProductId,x.Product?.Name,x.Product?.ImageUrl,x.Product?.PriceRetail,x.DiscountPercent)).ToArray(),e.CreatedAt,e.UpdatedAt,e.PublishedAt);
}
