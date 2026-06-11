namespace ShelfGuard.Application.Features.Weather.Dtos;

public sealed record WeatherDayDto(
    string Date,
    decimal? TempMin,
    decimal? TempMax,
    decimal? TempAvg,
    decimal? Precipitation,
    int? WeatherCode,
    bool IsForecast
);

public sealed record WeatherCoefficientDto(
    Guid Id,
    Guid? SegmentId,
    Guid? CategoryId,
    decimal? TempAbove,
    decimal? TempBelow,
    int? WeatherCode,
    decimal Coefficient,
    string Source
);

public sealed record CreateWeatherCoefficientRequest(
    Guid? SegmentId,
    Guid? CategoryId,
    decimal? TempAbove,
    decimal? TempBelow,
    int? WeatherCode,
    decimal Coefficient
);

public sealed record UpdateWeatherCoefficientRequest(decimal Coefficient);

public sealed record FetchWeatherResult(int StoresProcessed, int DaysUpserted, List<string> Errors);
