using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IWeatherRepository
{
    Task<List<WeatherData>> GetForecastAsync(Guid storeId, CancellationToken ct = default);
    Task<List<WeatherData>> GetHistoryAsync(Guid storeId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<WeatherData?> GetForDateAsync(Guid storeId, DateOnly date, CancellationToken ct = default);

    /// <summary>Locations of the tenant context that have coordinates set.</summary>
    Task<List<Location>> GetStoresWithCoordinatesAsync(CancellationToken ct = default);

    /// <summary>Existing rows for a store keyed by date (for upsert).</summary>
    Task<Dictionary<DateOnly, WeatherData>> GetByStoreDatesAsync(
        Guid storeId, IReadOnlyCollection<DateOnly> dates, CancellationToken ct = default);

    Task<List<WeatherCoefficient>> GetCoefficientsAsync(CancellationToken ct = default);
    Task<WeatherCoefficient?> GetCoefficientAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(WeatherData data, CancellationToken ct = default);
    Task AddCoefficientAsync(WeatherCoefficient coefficient, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
