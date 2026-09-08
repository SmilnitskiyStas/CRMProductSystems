using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShelfGuard.Application.Features.Weather;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Weather;

/// <summary>
/// TASK-708: WeatherService.GetMonthAsync — reads stored weather_data for a calendar month,
/// gap-fills missing/stale days from Open-Meteo (forecast API for recent months, archive API for
/// old ones), upserts, and returns the month sorted by date.
/// </summary>
public sealed class WeatherServiceGetMonthTests
{
    private readonly IWeatherRepository _repo = Substitute.For<IWeatherRepository>();
    private readonly IOpenMeteoClient _meteo = Substitute.For<IOpenMeteoClient>();
    private readonly WeatherService _sut;

    private readonly Guid _locationId = Guid.NewGuid();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public WeatherServiceGetMonthTests()
    {
        _sut = new WeatherService(_repo, _meteo, NullLogger<WeatherService>.Instance);

        _repo.GetByStoreDatesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<DateOnly>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<DateOnly, WeatherData>());
    }

    private void LocationHasCoords() =>
        _repo.GetLocationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Location { Latitude = 50.45m, Longitude = 30.52m });

    private static WeatherData Stored(Guid locationId, DateOnly date) => new()
    {
        StoreId = locationId,
        Date = date,
        TempMin = 4m,
        TempMax = 12m,
        TempAvg = 8m,
        FetchedAt = DateTime.UtcNow, // fresh
    };

    private static List<DateOnly> DaysOf(DateOnly monthStart)
    {
        var end = monthStart.AddMonths(1).AddDays(-1);
        var days = new List<DateOnly>();
        for (var d = monthStart; d <= end; d = d.AddDays(1))
            days.Add(d);
        return days;
    }

    private static List<DailyForecast> Forecasts(IEnumerable<DateOnly> dates) =>
        dates.Select(d => new DailyForecast(d, 6m, 16m, 1.2m, 3)).ToList();

    // ── no coordinates → empty, no Open-Meteo call ─────────────────────────

    [Fact]
    public async Task GetMonthAsync_NoCoordinates_ReturnsEmpty_AndSkipsOpenMeteo()
    {
        _repo.GetLocationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Location { Latitude = null, Longitude = null });

        var result = await _sut.GetMonthAsync(_locationId, Today.Year, Today.Month);

        Assert.Empty(result);
        await _meteo.DidNotReceiveWithAnyArgs().GetRangeAsync(0, 0, 0, 0);
        await _meteo.DidNotReceiveWithAnyArgs().GetArchiveAsync(0, 0, default, default);
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(null!);
    }

    [Fact]
    public async Task GetMonthAsync_InvalidMonth_ReturnsEmpty()
    {
        var result = await _sut.GetMonthAsync(_locationId, Today.Year, 13);

        Assert.Empty(result);
        await _repo.DidNotReceiveWithAnyArgs().GetLocationAsync(default);
    }

    // ── gap-fill: only missing serviceable days fetched + upserted ─────────

    [Fact]
    public async Task GetMonthAsync_GapFill_FetchesAndAddsOnlyMissingDays()
    {
        LocationHasCoords();

        var monthStart = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1); // previous month, fully serviceable
        var allDays = DaysOf(monthStart);
        var storedDays = allDays.Take(10).ToList();
        var missingDays = allDays.Skip(10).ToList();

        var storedRows = storedDays.Select(d => Stored(_locationId, d)).ToList();
        _repo.GetRangeAsync(_locationId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(storedRows);

        // Open-Meteo returns the whole month; the service must upsert only the missing days.
        _meteo.GetRangeAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(),
                Arg.Is<int>(p => p >= 0 && p <= 92),
                Arg.Is<int>(f => f >= 1 && f <= 16),
                Arg.Any<CancellationToken>())
            .Returns(Forecasts(allDays));

        await _sut.GetMonthAsync(_locationId, monthStart.Year, monthStart.Month);

        await _meteo.Received(1).GetRangeAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _meteo.DidNotReceiveWithAnyArgs().GetArchiveAsync(0, 0, default, default);

        await _repo.Received(missingDays.Count).AddAsync(Arg.Any<WeatherData>(), Arg.Any<CancellationToken>());
        foreach (var d in storedDays)
            await _repo.DidNotReceive().AddAsync(Arg.Is<WeatherData>(w => w.Date == d), Arg.Any<CancellationToken>());

        // Past month → every upserted row is an actual, not a forecast; TempAvg = round((6+16)/2, 1).
        await _repo.Received().AddAsync(
            Arg.Is<WeatherData>(w => w.Date == missingDays[0] && !w.IsForecast && w.TempAvg == 11m),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMonthAsync_AllStoredAndFresh_DoesNotCallOpenMeteo()
    {
        LocationHasCoords();

        var monthStart = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var rows = DaysOf(monthStart).Select(d => Stored(_locationId, d)).ToList();
        _repo.GetRangeAsync(_locationId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(rows);

        var result = await _sut.GetMonthAsync(_locationId, monthStart.Year, monthStart.Month);

        Assert.Equal(rows.Count, result.Count);
        await _meteo.DidNotReceiveWithAnyArgs().GetRangeAsync(0, 0, 0, 0);
        await _meteo.DidNotReceiveWithAnyArgs().GetArchiveAsync(0, 0, default, default);
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(null!);
    }

    // ── forecast flag: future days in the current month are forecasts ──────

    [Fact]
    public async Task GetMonthAsync_CurrentMonth_TodayUpsertedAsForecast()
    {
        LocationHasCoords();

        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        _repo.GetRangeAsync(_locationId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<WeatherData>());

        _meteo.GetRangeAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<DailyForecast> { new(Today, 10m, 20m, 0m, 1) });

        await _sut.GetMonthAsync(_locationId, monthStart.Year, monthStart.Month);

        await _repo.Received(1).AddAsync(
            Arg.Is<WeatherData>(w => w.Date == Today && w.IsForecast && w.TempAvg == 15m),
            Arg.Any<CancellationToken>());
    }

    // ── old month → archive API ───────────────────────────────────────────

    [Fact]
    public async Task GetMonthAsync_MonthOlderThan92Days_UsesArchiveApi()
    {
        LocationHasCoords();

        var monthStart = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-6); // entirely before today-92
        var allDays = DaysOf(monthStart);

        _repo.GetRangeAsync(_locationId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<WeatherData>());
        _meteo.GetArchiveAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Forecasts(allDays));

        await _sut.GetMonthAsync(_locationId, monthStart.Year, monthStart.Month);

        await _meteo.Received(1).GetArchiveAsync(
            Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _meteo.DidNotReceiveWithAnyArgs().GetRangeAsync(0, 0, 0, 0);
        await _repo.Received(allDays.Count).AddAsync(
            Arg.Is<WeatherData>(w => !w.IsForecast), Arg.Any<CancellationToken>());
    }

    // ── Open-Meteo failure → stored rows, no 500 ──────────────────────────

    [Fact]
    public async Task GetMonthAsync_OpenMeteoThrows_ReturnsStoredRows_AndDoesNotSave()
    {
        LocationHasCoords();

        var monthStart = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var storedRows = DaysOf(monthStart).Take(5).Select(d => Stored(_locationId, d)).ToList();
        _repo.GetRangeAsync(_locationId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(storedRows);
        _meteo.GetRangeAsync(
                Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("open-meteo down"));

        var result = await _sut.GetMonthAsync(_locationId, monthStart.Year, monthStart.Month);

        Assert.Equal(5, result.Count);
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(null!);
        await _repo.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }
}
