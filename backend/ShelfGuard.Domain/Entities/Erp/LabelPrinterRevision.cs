namespace ShelfGuard.Domain.Entities.Erp;
public sealed class LabelPrinterRevision
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProfileKey { get; set; }
    public int Version { get; set; }
    public Guid LocationId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string ContentHash { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Payload { get; set; } = "";
}
