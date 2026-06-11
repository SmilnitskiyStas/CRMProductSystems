using ShelfGuard.Application.Features.Weather.Dtos;

namespace ShelfGuard.Application.Features.Weather;

public interface IWeatherService
{
    Task<List<WeatherDayDto>> GetForecastAsync(Guid storeId, CancellationToken ct = default);
    Task<List<WeatherDayDto>> GetHistoryAsync(Guid storeId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Fetches a 7-day forecast from Open-Meteo for every store with coordinates and upserts weather_data.</summary>
    Task<FetchWeatherResult> FetchAsync(CancellationToken ct = default);

    Task<List<WeatherCoefficientDto>> GetCoefficientsAsync(CancellationToken ct = default);

    Task<(WeatherCoefficientDto? Coefficient, string? Error)> CreateCoefficientAsync(
        Guid tenantId, CreateWeatherCoefficientRequest request, CancellationToken ct = default);

    Task<(WeatherCoefficientDto? Coefficient, string? Error)> UpdateCoefficientAsync(
        Guid id, UpdateWeatherCoefficientRequest request, CancellationToken ct = default);
}
