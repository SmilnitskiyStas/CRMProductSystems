using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class PromotionCampaignRepository : IPromotionCampaignRepository
{
    private readonly AppDbContext _db;
    public PromotionCampaignRepository(AppDbContext db) => _db = db;

    public Task<List<PromotionCampaign>> GetAllAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.PromotionCampaigns.AsNoTracking().Where(x => x.TenantId == tenantId)
            .Include(x => x.Locations).Include(x => x.Products)
            .OrderBy(x => x.SortOrder).ThenByDescending(x => x.CreatedAt).ToListAsync(ct);

    public Task<PromotionCampaign?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _db.PromotionCampaigns.Where(x => x.TenantId == tenantId && x.Id == id)
            .Include(x => x.Locations).Include(x => x.Products).ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(ct);

    public Task AddAsync(PromotionCampaign campaign, CancellationToken ct = default) =>
        _db.PromotionCampaigns.AddAsync(campaign, ct).AsTask();

    public async Task ReplaceLocationsAsync(Guid tenantId, Guid campaignId, IReadOnlyCollection<Guid> locationIds, CancellationToken ct = default)
    {
        await _db.PromotionCampaignLocations.Where(x => x.TenantId == tenantId && x.CampaignId == campaignId).ExecuteDeleteAsync(ct);
        await _db.PromotionCampaignLocations.AddRangeAsync(locationIds.Distinct().Select(id => new PromotionCampaignLocation { TenantId = tenantId, CampaignId = campaignId, LocationId = id }), ct);
    }

    public async Task ReplaceProductsAsync(Guid tenantId, Guid campaignId, IReadOnlyCollection<(Guid ProductId, decimal DiscountPercent)> products, CancellationToken ct = default)
    {
        await _db.PromotionCampaignProducts.Where(x => x.TenantId == tenantId && x.CampaignId == campaignId).ExecuteDeleteAsync(ct);
        await _db.PromotionCampaignProducts.AddRangeAsync(products.GroupBy(x => x.ProductId).Select(x => new PromotionCampaignProduct { TenantId = tenantId, CampaignId = campaignId, ProductId = x.Key, DiscountPercent = x.Last().DiscountPercent }), ct);
    }

    public Task<List<Discount>> GetCampaignDiscountsAsync(Guid tenantId, Guid campaignId, CancellationToken ct = default) =>
        _db.Discounts.Where(x => x.TenantId == tenantId && x.PromotionCampaignId == campaignId).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
