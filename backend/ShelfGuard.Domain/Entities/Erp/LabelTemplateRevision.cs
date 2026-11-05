namespace ShelfGuard.Domain.Entities.Erp;

// Immutable development layouts. No price, printer address or executable rendering payload.
public sealed class LabelTemplateRevision
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TemplateKey { get; set; }
    public int Version { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string ContentHash { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Payload { get; set; } = "";
}
public sealed class LabelStoreAssignment
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LocationId { get; set; }
    public int Version { get; set; }
    public Guid? RevisionId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string RequestHash { get; set; } = "";
}
