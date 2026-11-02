namespace ShelfGuard.Domain.Entities.Erp;

/// <summary>A consumer inserts its completed marker in the same transaction as its effects.</summary>
public sealed class InboxDeduplication
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Consumer { get; init; } = string.Empty;
    public Guid MessageId { get; init; }
    public string PayloadHash { get; init; } = string.Empty;
    public string? Result { get; init; }
    public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;
}
