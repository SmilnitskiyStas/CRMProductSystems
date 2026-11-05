namespace ShelfGuard.Domain.Entities.Erp;

/// <summary>One version of a logical document. A revision creates a new Id with the same DocumentKey.</summary>
public sealed class BusinessDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid DocumentKey { get; init; } = Guid.NewGuid();
    public int Version { get; init; } = 1;
    public string DocumentType { get; init; } = string.Empty;
    public string? Number { get; set; }
    public string Status { get; set; } = "draft";
    public Guid? LegalEntityId { get; init; }
    public string SourceSystem { get; init; } = "erp";
    public string? SourceId { get; init; }
    public string Currency { get; init; } = "UAH";
    public Guid? ReversesDocumentId { get; init; }
    public Guid? CreatedBy { get; init; }
    public string? ContentHash { get; set; }
    public DateTime? OccurredAt { get; set; }
    public DateTime? PostedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
