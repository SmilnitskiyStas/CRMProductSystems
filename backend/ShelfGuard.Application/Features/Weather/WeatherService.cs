using ShelfGuard.Application.Features.Weather.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Weather;

public sealed class WeatherService : IWeatherService
{
    private readonly IWeatherRepository _repo;
    private readonly IOpenMeteoClient _meteo;

    public WeatherService(IWeatherRepository repo, IOpenMeteoClient meteo)
    {
        _repo = repo;
        _meteo = meteo;
    }

    public async Task<List<WeatherDayDto>> GetForecastAsync(Guid storeId, CancellationToken ct = default)
    {
        var rows = await _repo.GetForecastAsync(storeId, ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<List<WeatherDayDto>> GetHistoryAsync(
        Guid storeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await _repo.GetHistoryAsync(storeId, from, to, ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<FetchWeatherResult> FetchAsync(CancellationToken ct = default)
    {
        var stores = await _repo.GetStoresWithCoordinatesAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int upserted = 0;
        var errors = new List<string>();

        foreach (var store in stores)
        {
            List<DailyForecast> forecast;
            try
            {
                forecast = await _meteo.GetForecastAsync(store.Latitude!.Value, store.Longitude!.Value, 7, ct);
            }
            catch (Exception ex)
            {
                errors.Add($"{store.Name}: {ex.Message}");
                continue;
            }

            var existing = await _repo.GetByStoreDatesAsync(
                store.Id, forecast.Select(f => f.Date).ToList(), ct);

            foreach (var day in forecast)
            {
                decimal? tempAvg = day.TempMin is not null && day.TempMax is not null
                    ? Math.Round((day.TempMin.Value + day.TempMax.Value) / 2, 1)
                    : null;

                if (existing.TryGetValue(day.Date, out var row))
                {
                    row.TempMin = day.TempMin;
                    row.TempMax = day.TempMax;
                    row.TempAvg = tempAvg;
                    row.Precipitation = day.Precipitation;
                    row.WeatherCode = day.WeatherCode;
                    row.IsForecast = day.Date >= today;
                    row.FetchedAt = DateTime.UtcNow;
                }
                else
                {
                    await _repo.AddAsync(new WeatherData
                    {
                        StoreId = store.Id,
                        Date = day.Date,
                        TempMin = day.TempMin,
                        TempMax = day.TempMax,
                        TempAvg = tempAvg,
                        Precipitation = day.Precipitation,
                        WeatherCode = day.WeatherCode,
                        IsForecast = day.Date >= today,
                    }, ct);
                }
                upserted++;
            }
        }

        await _repo.SaveChangesAsync(ct);
        return new FetchWeatherResult(stores.Count, upserted, errors);
    }

    public async Task<List<WeatherCoefficientDto>> GetCoefficientsAsync(CancellationToken ct = default)
    {
        var coefs = await _repo.GetCoefficientsAsync(ct);
        return coefs.Select(ToDto).ToList();
    }

    public async Task<(WeatherCoefficientDto? Coefficient, string? Error)> CreateCoefficientAsync(
        Guid tenantId, CreateWeatherCoefficientRequest request, CancellationToken ct = default)
    {
        if (request.Coefficient is <= 0 or > 10)
            return (null, "Coefficient must be between 0.01 and 10.");
        if (request.TempAbove is null && request.TempBelow is null && request.WeatherCode is null)
            return (null, "At least one condition (TempAbove, TempBelow or WeatherCode) is required.");

        var coef = new WeatherCoefficient
        {
            TenantId = tenantId,
            SegmentId = request.SegmentId,
            CategoryId = request.CategoryId,
            TempAbove = request.TempAbove,
            TempBelow = request.TempBelow,
            WeatherCode = request.WeatherCode,
            Coefficient = request.Coefficient,
            Source = "manual",
        };

        await _repo.AddCoefficientAsync(coef, ct);
        await _repo.SaveChangesAsync(ct);
        return (ToDto(coef), null);
    }

    public async Task<(WeatherCoefficientDto? Coefficient, string? Error)> UpdateCoefficientAsync(
        Guid id, UpdateWeatherCoefficientRequest request, CancellationToken ct = default)
    {
        var coef = await _repo.GetCoefficientAsync(id, ct);
        if (coef is null) return (null, "Weather coefficient not found.");
        if (request.Coefficient is <= 0 or > 10)
            return (null, "Coefficient must be between 0.01 and 10.");

        coef.Coefficient = request.Coefficient;
        coef.Source = "manual";
        await _repo.SaveChangesAsync(ct);
        return (ToDto(coef), null);
    }

    private static WeatherDayDto ToDto(WeatherData w) => new(
        w.Date.ToString("yyyy-MM-dd"), w.TempMin, w.TempMax, w.TempAvg,
        w.Precipitation, w.WeatherCode, w.IsForecast);

    private static WeatherCoefficientDto ToDto(WeatherCoefficient c) => new(
        c.Id, c.SegmentId, c.CategoryId, c.TempAbove, c.TempBelow,
        c.WeatherCode, c.Coefficient, c.Source);
}

/// <summary>
/// Weather multiplier for a product (v2-spec §6).
/// A rule matches when its temperature/code condition holds for the day AND its
/// scope matches the product (SegmentId/CategoryId set) or is global (both null).
/// Multiple matching rules multiply.
/// </summary>
public static class WeatherCoefficientResolver
{
    public static decimal Resolve(
        WeatherData? day,
        IReadOnlyList<WeatherCoefficient> rules,
        Guid? segmentId, Guid? categoryId)
    {
        if (day is null || rules.Count == 0) return 1m;

        var total = 1m;
        foreach (var rule in rules)
        {
            var conditionMet =
                (rule.TempAbove is not null && day.TempMax is not null && day.TempMax > rule.TempAbove)
                || (rule.TempBelow is not null && day.TempMax is not null && day.TempMax < rule.TempBelow)
                || (rule.WeatherCode is not null && day.WeatherCode == rule.WeatherCode);

            if (!conditionMet) continue;

            var scopeMatches =
                (rule.SegmentId is null && rule.CategoryId is null) // global rule
                || (rule.SegmentId is not null && rule.SegmentId == segmentId)
                || (rule.CategoryId is not null && rule.CategoryId == categoryId);

            if (scopeMatches)
                total *= rule.Coefficient;
        }

        return Math.Round(total, 2);
    }
}
