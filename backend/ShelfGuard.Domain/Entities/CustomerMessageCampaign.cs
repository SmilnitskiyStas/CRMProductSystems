namespace ShelfGuard.Domain.Entities;

/// <summary>Canonical outbound campaign. NotificationQueue remains a delivery outbox; campaign
/// metadata and its frozen audience live here so history never depends on mutable analytics.</summary>
public sealed class CustomerMessageCampaign
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid CreatedByUserId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string AudienceSource { get; init; } = string.Empty;
    public string AudienceDefinition { get; init; } = "{}";
    public List<string> Channels { get; init; } = [];
    public string? MessengerProvider { get; init; }
    public string? ContentType { get; init; }
    public Guid? ContentId { get; init; }
    public string? ContentTitle { get; init; }
    public string? ContentImageUrl { get; init; }
    public string DeliveryMode { get; set; } = "draft";
    public DateTime? ScheduledAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public int EstimatedRecipients { get; init; }
    public int ResolvedRecipients { get; init; }
    public string Status { get; set; } = "draft";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Tenant? Tenant { get; init; }
    public User? CreatedByUser { get; init; }
    public ICollection<CustomerMessageRecipient> Recipients { get; init; } = new List<CustomerMessageRecipient>();
}
