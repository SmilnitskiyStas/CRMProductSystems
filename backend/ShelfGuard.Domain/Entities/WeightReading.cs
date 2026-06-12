namespace ShelfGuard.Domain.Entities;

public sealed class WeightReading
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid DeviceId { get; init; }
    public Guid? ZoneId { get; init; }
    public decimal? WeightBefore { get; init; }
    public decimal? WeightAfter { get; init; }
    public decimal? DeltaWeight { get; init; }
    // Whether the delta has already been turned into a stock_event / FEFO write-down
    public bool Processed { get; set; }
    public int? Confidence { get; init; }
    public DateTime RecordedAt { get; init; }

    // Navigation properties
    public IotDevice? Device { get; init; }
}
