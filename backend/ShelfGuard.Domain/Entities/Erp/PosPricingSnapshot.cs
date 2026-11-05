namespace ShelfGuard.Domain.Entities.Erp;

/// <summary>Append-only decision archive, inserted in the sale's own commit. No retrospective backfill.</summary>
public sealed class PosPricingSnapshot
{
    public Guid TransactionId { get; init; }
    public Guid TenantId { get; init; }
    public Guid StoreId { get; init; }
    public long OriginTransactionId { get; private set; }
    public string Protocol { get; init; } = "pos.pricing.archive.v1";
    public string Channel { get; init; } = "";
    public DateTime EvaluatedAt { get; init; }
    public DateTime RecordedAt { get; init; } = DateTime.UtcNow;
    public string ContentHash { get; init; } = "";
    // PostgreSQL json preserves the exact UTF-8 document used for ContentHash (jsonb does not).
    public string Payload { get; init; } = "{}";
}
