using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class WeatherRepository : IWeatherRepository
{
    private readonly AppDbContext _db;

    public WeatherRepository(AppDbContext db) => _db = db;

    public Task<List<WeatherData>> GetForecastAsync(Guid storeId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return _db.WeatherData
            .Where(w => w.StoreId == storeId && w.Date >= today)
            .OrderBy(w => w.Date)
            .ToListAsync(ct);
    }

    public Task<List<WeatherData>> GetHistoryAsync(
        Guid storeId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        _db.WeatherData
            .Where(w => w.StoreId == storeId && w.Date >= from && w.Date <= to)
            .OrderBy(w => w.Date)
            .ToListAsync(ct);

    public Task<WeatherData?> GetForDateAsync(Guid storeId, DateOnly date, CancellationToken ct = default) =>
        _db.WeatherData.FirstOrDefaultAsync(w => w.StoreId == storeId && w.Date == date, ct);

    public Task<List<Store>> GetStoresWithCoordinatesAsync(CancellationToken ct = default) =>
        _db.Stores
            .Where(s => s.IsActive && s.Latitude != null && s.Longitude != null)
            .ToListAsync(ct);

    public async Task<Dictionary<DateOnly, WeatherData>> GetByStoreDatesAsync(
        Guid storeId, IReadOnlyCollection<DateOnly> dates, CancellationToken ct = default)
    {
        var rows = await _db.WeatherData
            .Where(w => w.StoreId == storeId && dates.Contains(w.Date))
            .ToListAsync(ct);
        return rows.ToDictionary(w => w.Date);
    }

    public Task<List<WeatherCoefficient>> GetCoefficientsAsync(CancellationToken ct = default) =>
        _db.WeatherCoefficients.ToListAsync(ct);

    public Task<WeatherCoefficient?> GetCoefficientAsync(Guid id, CancellationToken ct = default) =>
        _db.WeatherCoefficients.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(WeatherData data, CancellationToken ct = default) =>
        await _db.WeatherData.AddAsync(data, ct);

    public async Task AddCoefficientAsync(WeatherCoefficient coefficient, CancellationToken ct = default) =>
        await _db.WeatherCoefficients.AddAsync(coefficient, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
