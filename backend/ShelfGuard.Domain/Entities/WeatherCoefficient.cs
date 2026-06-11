namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Weather-driven order multiplier (v2-spec §6) for a segment or category.
/// Applies when temp_max > TempAbove, or temp_max < TempBelow, or WeatherCode matches.
/// </summary>
public sealed class WeatherCoefficient
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid? SegmentId { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal? TempAbove { get; set; }
    public decimal? TempBelow { get; set; }
    public int? WeatherCode { get; set; }
    public decimal Coefficient { get; set; } = 1.00m;
    public string Source { get; set; } = "manual";

    public ProductSegment? Segment { get; init; }
    public Category? Category { get; init; }
}
