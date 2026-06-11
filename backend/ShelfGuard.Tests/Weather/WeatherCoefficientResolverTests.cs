using ShelfGuard.Application.Features.Weather;
using ShelfGuard.Domain.Entities;
using Xunit;

namespace ShelfGuard.Tests.Weather;

public sealed class WeatherCoefficientResolverTests
{
    private static readonly Guid SegWater = Guid.NewGuid();
    private static readonly Guid CatDrinks = Guid.NewGuid();

    private static WeatherData Day(decimal? tempMax, int? code = null, decimal? precip = null) => new()
    {
        StoreId = Guid.Empty,
        Date = new DateOnly(2026, 6, 11),
        TempMax = tempMax,
        WeatherCode = code,
        Precipitation = precip,
    };

    private static WeatherCoefficient Rule(
        decimal k, decimal? above = null, decimal? below = null, int? code = null,
        Guid? segment = null, Guid? category = null) => new()
    {
        Coefficient = k, TempAbove = above, TempBelow = below,
        WeatherCode = code, SegmentId = segment, CategoryId = category,
    };

    [Fact]
    public void No_weather_data_is_neutral()
    {
        var k = WeatherCoefficientResolver.Resolve(null, [Rule(1.8m, above: 25)], SegWater, CatDrinks);
        Assert.Equal(1m, k);
    }

    [Fact]
    public void Hot_day_boosts_matching_segment()
    {
        var rules = new[] { Rule(1.8m, above: 25, segment: SegWater) };

        Assert.Equal(1.8m, WeatherCoefficientResolver.Resolve(Day(30), rules, SegWater, null));
        Assert.Equal(1m, WeatherCoefficientResolver.Resolve(Day(20), rules, SegWater, null));   // not hot
        Assert.Equal(1m, WeatherCoefficientResolver.Resolve(Day(30), rules, Guid.NewGuid(), null)); // other segment
    }

    [Fact]
    public void Cold_day_uses_temp_below()
    {
        var rules = new[] { Rule(1.5m, below: 0, category: CatDrinks) };

        Assert.Equal(1.5m, WeatherCoefficientResolver.Resolve(Day(-5), rules, null, CatDrinks));
        Assert.Equal(1m, WeatherCoefficientResolver.Resolve(Day(10), rules, null, CatDrinks));
    }

    [Fact]
    public void Global_rule_applies_to_everything()
    {
        // Rain (weathercode 61) → ×0.85 store traffic, no scope
        var rules = new[] { Rule(0.85m, code: 61) };

        Assert.Equal(0.85m, WeatherCoefficientResolver.Resolve(Day(15, code: 61), rules, SegWater, CatDrinks));
        Assert.Equal(0.85m, WeatherCoefficientResolver.Resolve(Day(15, code: 61), rules, null, null));
        Assert.Equal(1m, WeatherCoefficientResolver.Resolve(Day(15, code: 0), rules, null, null));
    }

    [Fact]
    public void Multiple_matching_rules_multiply()
    {
        var rules = new[]
        {
            Rule(1.8m, above: 25, segment: SegWater), // heat boost
            Rule(0.85m, code: 61),                    // rain dampener (global)
        };

        var k = WeatherCoefficientResolver.Resolve(Day(28, code: 61), rules, SegWater, null);

        Assert.Equal(1.53m, k); // 1.8 × 0.85
    }
}
