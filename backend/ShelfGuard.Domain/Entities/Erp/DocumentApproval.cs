namespace ShelfGuard.Domain.Entities.Erp;

public sealed class DocumentApproval
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid DocumentId { get; init; }
    public int Stage { get; init; } = 1;
    public Guid ApproverId { get; init; }
    public string Decision { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
