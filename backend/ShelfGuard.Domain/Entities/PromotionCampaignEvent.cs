namespace ShelfGuard.Domain.Entities;

public sealed class PromotionCampaignEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid CampaignId { get; init; }
    public Guid StoreId { get; init; }
    public Guid? ConsumerAccountId { get; init; }
    public string EventType { get; init; } = PromotionCampaignEventType.Impression;
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

public static class PromotionCampaignEventType
{
    public const string Impression = "impression";
    public const string Open = "open";
    public static bool IsValid(string value) => value is Impression or Open;
}
