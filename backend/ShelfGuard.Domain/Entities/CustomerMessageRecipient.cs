namespace ShelfGuard.Domain.Entities;

/// <summary>Frozen, deduplicated audience member. No phone/email is copied: delivery resolves
/// current consent and contact data from the tenant-owned customer when a provider is invoked.</summary>
public sealed class CustomerMessageRecipient
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid CampaignId { get; init; }
    public Guid CustomerId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public CustomerMessageCampaign? Campaign { get; init; }
    public Customer? Customer { get; init; }
}
