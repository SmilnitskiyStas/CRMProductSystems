using ShelfGuard.Application.Features.WeatherPromo.Dtos;
using ShelfGuard.Infrastructure.AI.WeatherPromoAdvisor;
using Xunit;

namespace ShelfGuard.Tests.WeatherPromo;

/// <summary>
/// TASK-711 — WeatherPromoAdvisor.ParseSuggestions: internal + static so it can be exercised
/// without a real model call (same pattern as SupplierAdvisorTests).
/// </summary>
public sealed class WeatherPromoAdvisorParsingTests
{
    private static readonly Guid _itemA = Guid.NewGuid();
    private static readonly Guid _itemB = Guid.NewGuid();

    [Fact]
    public void ParseSuggestions_ValidJson_MapsToDtos()
    {
        var json = $$"""
        {
          "suggestions": [
            {
              "title": "Тепле сонце на вихідних",
              "weather_window": { "starts_at": "2026-09-12", "ends_at": "2026-09-19", "summary": "+27°C, ясно" },
              "rationale": "Спека підвищує попит на морозиво та напої.",
              "recommended_discount_pct": 15,
              "confidence": "high",
              "products": [
                { "item_id": "{{_itemA}}", "name": "Морозиво пломбір", "reason": "Погодозалежний топ" },
                { "item_id": "{{_itemB}}", "name": "Вода 1.5л", "reason": "Зростає в спеку" }
              ]
            }
          ]
        }
        """;

        var suggestions = WeatherPromoAdvisor.ParseSuggestions(json);

        var s = Assert.Single(suggestions);
        Assert.Equal("Тепле сонце на вихідних", s.Title);
        Assert.Equal(new DateOnly(2026, 9, 12), s.StartsAt);
        Assert.Equal(new DateOnly(2026, 9, 19), s.EndsAt);
        Assert.Equal("+27°C, ясно", s.WeatherSummary);
        Assert.Equal(15m, s.RecommendedDiscountPct);
        Assert.Equal("high", s.Confidence);
        Assert.Equal(2, s.Products.Count);
        Assert.Equal(_itemA, s.Products[0].ItemId);
        Assert.Null(s.Products[0].CurrentPrice); // service fills this from its top-seller lookup
    }

    [Fact]
    public void ParseSuggestions_ProductWithNonGuidItemId_IsSkipped()
    {
        var json = $$"""
        {
          "suggestions": [
            {
              "title": "Дощовий тиждень",
              "weather_window": { "starts_at": "2026-10-01", "ends_at": "2026-10-08", "summary": "Дощі, +9°C" },
              "rationale": "Тест.",
              "recommended_discount_pct": 10,
              "confidence": "medium",
              "products": [
                { "item_id": "not-a-guid", "name": "Парасолька", "reason": "..." },
                { "item_id": "{{_itemA}}", "name": "Гарячий чай", "reason": "Реальний товар" }
              ]
            }
          ]
        }
        """;

        var suggestions = WeatherPromoAdvisor.ParseSuggestions(json);

        var s = Assert.Single(suggestions);
        var p = Assert.Single(s.Products);
        Assert.Equal(_itemA, p.ItemId);
    }

    [Fact]
    public void ParseSuggestions_EmptySuggestions_ReturnsEmptyList()
    {
        const string json = """{ "suggestions": [] }""";

        Assert.Empty(WeatherPromoAdvisor.ParseSuggestions(json));
    }

    [Fact]
    public void ParseSuggestions_UnparseableWeatherWindow_SkipsSuggestion()
    {
        var json = $$"""
        {
          "suggestions": [
            {
              "title": "Погане вікно",
              "weather_window": { "starts_at": "невдовзі", "ends_at": "скоро", "summary": "тепло" },
              "rationale": "Тест.",
              "recommended_discount_pct": 12,
              "confidence": "low",
              "products": [ { "item_id": "{{_itemA}}", "name": "X", "reason": "Y" } ]
            },
            {
              "title": "Добре вікно",
              "weather_window": { "starts_at": "2026-09-20", "ends_at": "2026-09-27", "summary": "тепло" },
              "rationale": "Тест.",
              "recommended_discount_pct": 12,
              "confidence": "low",
              "products": [ { "item_id": "{{_itemA}}", "name": "X", "reason": "Y" } ]
            }
          ]
        }
        """;

        var suggestions = WeatherPromoAdvisor.ParseSuggestions(json);

        Assert.Single(suggestions);
        Assert.Equal("Добре вікно", suggestions[0].Title);
    }
}
