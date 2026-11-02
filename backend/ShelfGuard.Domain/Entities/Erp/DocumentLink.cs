namespace ShelfGuard.Domain.Entities.Erp;

public sealed class DocumentLink
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid FromDocumentId { get; init; }
    public Guid ToDocumentId { get; init; }
    public string RelationType { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
