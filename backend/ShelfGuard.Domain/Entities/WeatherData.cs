namespace ShelfGuard.Domain.Entities;

/// <summary>Daily weather per store from Open-Meteo (v2-spec §6). Forecast or actual.</summary>
public sealed class WeatherData
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid StoreId { get; init; }
    public DateOnly Date { get; init; }
    public decimal? TempMin { get; set; }
    public decimal? TempMax { get; set; }
    public decimal? TempAvg { get; set; }
    public decimal? Precipitation { get; set; }
    public int? WeatherCode { get; set; }
    public bool IsForecast { get; set; } = true;
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    public Location? Store { get; init; }
}
