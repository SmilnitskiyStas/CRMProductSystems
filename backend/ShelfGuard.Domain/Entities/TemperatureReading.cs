namespace ShelfGuard.Domain.Entities;

public sealed class TemperatureReading
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DeviceId { get; init; }
    public Guid StoreId { get; init; }
    public Guid? ZoneId { get; init; }
    public decimal Temperature { get; init; }
    public decimal? Humidity { get; init; }
    public bool IsAlert { get; init; }
    public DateTime RecordedAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Navigation properties
    public IotDevice? Device { get; init; }
}
