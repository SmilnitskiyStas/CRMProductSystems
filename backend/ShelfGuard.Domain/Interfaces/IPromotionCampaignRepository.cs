using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IPromotionCampaignRepository
{
    Task<List<PromotionCampaign>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<PromotionCampaign?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task AddAsync(PromotionCampaign campaign, CancellationToken ct = default);
    Task ReplaceLocationsAsync(Guid tenantId, Guid campaignId, IReadOnlyCollection<Guid> locationIds, CancellationToken ct = default);
    Task ReplaceProductsAsync(Guid tenantId, Guid campaignId, IReadOnlyCollection<(Guid ProductId, decimal DiscountPercent)> products, CancellationToken ct = default);
    Task<List<Discount>> GetCampaignDiscountsAsync(Guid tenantId, Guid campaignId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
