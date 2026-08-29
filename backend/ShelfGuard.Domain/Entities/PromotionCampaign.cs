namespace ShelfGuard.Domain.Entities;

/// <summary>A customer-facing promotion that owns its news card, audience, stores and discounted products.</summary>
public sealed class PromotionCampaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Eyebrow { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Terms { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string BackgroundColor { get; set; } = "#14532D";
    public string AccentColor { get; set; } = "#86EFAC";
    public string AudienceType { get; set; } = PromotionAudienceType.All;
    public string AudienceTierIdsJson { get; set; } = "[]";
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string Status { get; set; } = PromotionCampaignStatus.Draft;
    public int SortOrder { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }

    public ICollection<PromotionCampaignLocation> Locations { get; init; } = new List<PromotionCampaignLocation>();
    public ICollection<PromotionCampaignProduct> Products { get; init; } = new List<PromotionCampaignProduct>();
}

public static class PromotionAudienceType
{
    public const string All = "all";
    public const string LoyaltyMembers = "loyalty_members";
    public const string LoyaltyTiers = "loyalty_tiers";
    public static readonly IReadOnlySet<string> AllValues = new HashSet<string> { All, LoyaltyMembers, LoyaltyTiers };
}

public static class PromotionCampaignStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Cancelled = "cancelled";
}
