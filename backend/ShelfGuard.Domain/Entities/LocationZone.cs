namespace ShelfGuard.Domain.Entities;

public sealed class LocationZone
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LocationId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "shelf";
    public string? Position { get; set; }
    public int ShelvesCount { get; set; } = 1;
    public decimal? TempMin { get; set; }
    public decimal? TempMax { get; set; }
    public bool IsActive { get; set; } = true;

    public Location? Location { get; init; }
}
