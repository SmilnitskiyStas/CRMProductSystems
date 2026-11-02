namespace ShelfGuard.Domain.Entities;

/// <summary>Durable ingestion identity, retained independently of sensor/event log retention.</summary>
public sealed class IotMessageReceipt
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid DeviceId { get; init; }
    public string MessageKey { get; init; } = string.Empty;
    public string PayloadHash { get; init; } = string.Empty;
    public DateTime RecordedAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
