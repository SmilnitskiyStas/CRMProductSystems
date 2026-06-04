namespace ShelfGuard.Domain.Entities;

public sealed class StockEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public Guid? ProductStockId { get; init; }
    public Guid? ProductId { get; init; }
    public Guid? StoreId { get; init; }
    public string? SourceDeviceId { get; init; }
    public decimal? QuantityDelta { get; init; }
    public int Confidence { get; init; } = 100;
    public string? Meta { get; init; }
    public Guid? PerformedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
