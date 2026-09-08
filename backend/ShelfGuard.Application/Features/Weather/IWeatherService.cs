using ShelfGuard.Application.Features.Weather.Dtos;

namespace ShelfGuard.Application.Features.Weather;

public interface IWeatherService
{
    Task<List<WeatherDayDto>> GetForecastAsync(Guid storeId, CancellationToken ct = default);
    Task<List<WeatherDayDto>> GetHistoryAsync(Guid storeId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Daily weather for a whole calendar month at a location (events-calendar cells + day detail).
    /// Reads stored <c>weather_data</c>, gap-fills missing/stale days from Open-Meteo (forecast API
    /// with past_days+forecast_days, or the archive API for months older than 92 days), upserts, and
    /// returns the month sorted by date. Empty list when the location has no coordinates; on an
    /// Open-Meteo failure it logs and returns whatever is already stored.
    /// </summary>
    Task<List<WeatherDayDto>> GetMonthAsync(Guid locationId, int year, int month, CancellationToken ct = default);

    /// <summary>Fetches a 7-day forecast from Open-Meteo for every store with coordinates and upserts weather_data.</summary>
    Task<FetchWeatherResult> FetchAsync(CancellationToken ct = default);

    Task<List<WeatherCoefficientDto>> GetCoefficientsAsync(CancellationToken ct = default);

    Task<(WeatherCoefficientDto? Coefficient, string? Error)> CreateCoefficientAsync(
        Guid tenantId, CreateWeatherCoefficientRequest request, CancellationToken ct = default);

    Task<(WeatherCoefficientDto? Coefficient, string? Error)> UpdateCoefficientAsync(
        Guid id, UpdateWeatherCoefficientRequest request, CancellationToken ct = default);
}
