namespace ShelfGuard.Domain.Interfaces;

public sealed record DailyForecast(
    DateOnly Date,
    decimal? TempMin,
    decimal? TempMax,
    decimal? Precipitation,
    int? WeatherCode);

/// <summary>Weather forecast provider abstraction. Implemented by Open-Meteo in Infrastructure.</summary>
public interface IOpenMeteoClient
{
    Task<List<DailyForecast>> GetForecastAsync(
        decimal latitude, decimal longitude, int days = 7, CancellationToken ct = default);
}
