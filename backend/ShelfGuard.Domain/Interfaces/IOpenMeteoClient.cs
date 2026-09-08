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

    /// <summary>
    /// One forecast-API call spanning <paramref name="pastDays"/> days back (max 92) and
    /// <paramref name="forecastDays"/> days forward (max 16) from today — covers any month
    /// within that window in a single request.
    /// </summary>
    Task<List<DailyForecast>> GetRangeAsync(
        decimal latitude, decimal longitude, int pastDays, int forecastDays, CancellationToken ct = default);

    /// <summary>
    /// Historical daily weather for an explicit date range via the archive API — used for months
    /// entirely older than the forecast API's 92-day past window.
    /// </summary>
    Task<List<DailyForecast>> GetArchiveAsync(
        decimal latitude, decimal longitude, DateOnly from, DateOnly to, CancellationToken ct = default);
}
