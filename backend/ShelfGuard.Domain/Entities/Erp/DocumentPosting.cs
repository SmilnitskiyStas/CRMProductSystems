namespace ShelfGuard.Domain.Entities.Erp;

public sealed class DocumentPosting
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid DocumentId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public Guid? ReversesPostingId { get; init; }
    public Guid? PerformedBy { get; init; }
    public DateTime PostedAt { get; init; } = DateTime.UtcNow;
}
