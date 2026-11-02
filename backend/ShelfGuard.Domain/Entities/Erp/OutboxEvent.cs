namespace ShelfGuard.Domain.Entities.Erp;

public sealed class OutboxEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid? PostingId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public int SchemaVersion { get; init; } = 1;
    public string AggregateType { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public int AggregateVersion { get; init; } = 1;
    public int EventOrdinal { get; init; } = 1;
    public string Payload { get; init; } = "{}";
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public DateTime OccurredAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string DeliveryScope { get; init; } = "live";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    public Guid? LockToken { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? LastError { get; set; }
}
