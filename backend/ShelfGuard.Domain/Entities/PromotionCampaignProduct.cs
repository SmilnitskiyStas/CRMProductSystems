namespace ShelfGuard.Domain.Entities;

public sealed class PromotionCampaignProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ProductId { get; set; }
    public decimal DiscountPercent { get; set; }
    public PromotionCampaign? Campaign { get; init; }
    public Item? Product { get; init; }
}
