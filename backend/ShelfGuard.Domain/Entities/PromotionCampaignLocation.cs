namespace ShelfGuard.Domain.Entities;

public sealed class PromotionCampaignLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid LocationId { get; set; }
    public PromotionCampaign? Campaign { get; init; }
    public Location? Location { get; init; }
}
