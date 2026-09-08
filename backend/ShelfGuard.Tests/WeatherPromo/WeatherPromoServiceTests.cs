using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using ShelfGuard.Application.Features.Discounts;
using ShelfGuard.Application.Features.Discounts.Dtos;
using ShelfGuard.Application.Features.Events;
using ShelfGuard.Application.Features.Events.Dtos;
using ShelfGuard.Application.Features.WeatherPromo;
using ShelfGuard.Application.Features.WeatherPromo.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.WeatherPromo;

/// <summary>TASK-711 — WeatherPromoService: caching, status branches, apply fan-out.</summary>
public sealed class WeatherPromoServiceTests
{
    private readonly IWeatherPromoAdvisor _advisor = Substitute.For<IWeatherPromoAdvisor>();
    private readonly IWeatherPromoRepository _repo = Substitute.For<IWeatherPromoRepository>();
    private readonly IWeatherRepository _weatherRepo = Substitute.For<IWeatherRepository>();
    private readonly IEventService _events = Substitute.For<IEventService>();
    private readonly IDiscountService _discounts = Substitute.For<IDiscountService>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly WeatherPromoService _sut;

    private static readonly Guid _tenant = Guid.NewGuid();
    private static readonly Guid _user = Guid.NewGuid();

    public WeatherPromoServiceTests()
    {
        _sut = new WeatherPromoService(_advisor, _repo, _weatherRepo, _events, _discounts, _cache);
    }

    private static WeatherPromoContextData EmptyContext() =>
        new(Array.Empty<WeatherPromoForecastLocation>(),
            Array.Empty<WeatherPromoTopSellerLine>(),
            Array.Empty<WeatherPromoCoefficientLine>(),
            Array.Empty<string>(),
            Array.Empty<string>());

    private static WeatherPromoContextData ContextWithTopSeller(Guid itemId, decimal? price) =>
        new(Array.Empty<WeatherPromoForecastLocation>(),
            new[] { new WeatherPromoTopSellerLine(itemId, "Морозиво", "Молочні", price, 120m) },
            Array.Empty<WeatherPromoCoefficientLine>(),
            Array.Empty<string>(),
            Array.Empty<string>());

    private static WeatherPromoAdviceResult AdviceFor(Guid itemId, string model = "claude-test") =>
        new(new[]
        {
            new WeatherPromoSuggestion(
                "Тепла погода",
                new DateOnly(2026, 9, 12),
                new DateOnly(2026, 9, 19),
                "+27°C",
                "Спека → морозиво",
                15m,
                "high",
                new[] { new WeatherPromoProduct(itemId, "Морозиво", null, "погодозалежний топ") }),
        }, model, 100);

    // ── GetSuggestionsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetSuggestions_AdvisorNotConfigured_ReturnsNotConfigured_AndSkipsAdvisor()
    {
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.GetSuggestionsAsync(_tenant, refresh: false);

        Assert.Equal("not_configured", result.Status);
        Assert.Empty(result.Suggestions);
        await _advisor.DidNotReceive().RecommendAsync(Arg.Any<WeatherPromoContext>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetContextDataAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSuggestions_NoCache_NoRefresh_ReturnsNotGenerated_AndSkipsAdvisor()
    {
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.GetSuggestionsAsync(_tenant, refresh: false);

        Assert.Equal("not_generated", result.Status);
        Assert.Empty(result.Suggestions);
        await _advisor.DidNotReceive().RecommendAsync(Arg.Any<WeatherPromoContext>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetContextDataAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSuggestions_NoForecastAndNoSales_ReturnsInsufficientData()
    {
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetContextDataAsync(_tenant, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(EmptyContext());

        var result = await _sut.GetSuggestionsAsync(_tenant, refresh: true);

        Assert.Equal("insufficient_data", result.Status);
        await _advisor.DidNotReceive().RecommendAsync(Arg.Any<WeatherPromoContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSuggestions_HappyPath_FillsCurrentPrice_AndCaches()
    {
        var itemId = Guid.NewGuid();
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetContextDataAsync(_tenant, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(ContextWithTopSeller(itemId, 49.90m));
        _advisor.RecommendAsync(Arg.Any<WeatherPromoContext>(), Arg.Any<CancellationToken>())
            .Returns(AdviceFor(itemId));

        var first = await _sut.GetSuggestionsAsync(_tenant, refresh: true);

        Assert.Equal("ok", first.Status);
        Assert.NotNull(first.GeneratedAt);
        Assert.Equal("claude-test", first.Model);
        var product = Assert.Single(Assert.Single(first.Suggestions).Products);
        Assert.Equal(49.90m, product.CurrentPrice);

        // Second call (no refresh) is served from cache — advisor called only once.
        var second = await _sut.GetSuggestionsAsync(_tenant, refresh: false);
        Assert.Equal("ok", second.Status);
        await _advisor.Received(1).RecommendAsync(Arg.Any<WeatherPromoContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSuggestions_Refresh_BypassesCache()
    {
        var itemId = Guid.NewGuid();
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetContextDataAsync(_tenant, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(ContextWithTopSeller(itemId, 10m));
        _advisor.RecommendAsync(Arg.Any<WeatherPromoContext>(), Arg.Any<CancellationToken>())
            .Returns(AdviceFor(itemId));

        await _sut.GetSuggestionsAsync(_tenant, refresh: true);
        await _sut.GetSuggestionsAsync(_tenant, refresh: true);

        await _advisor.Received(2).RecommendAsync(Arg.Any<WeatherPromoContext>(), Arg.Any<CancellationToken>());
    }

    // ── ApplySuggestionAsync ────────────────────────────────────────────────

    private ApplySuggestionRequest ApplyRequest(
        IReadOnlyList<Guid> storeIds,
        IReadOnlyList<Guid> productIds,
        decimal pct = 15m,
        bool createEvent = true,
        bool createDiscounts = true,
        DateOnly? starts = null,
        DateOnly? ends = null) =>
        new("Погодна акція",
            starts ?? new DateOnly(2026, 9, 12),
            ends ?? new DateOnly(2026, 9, 19),
            pct, storeIds, productIds, createEvent, createDiscounts);

    private static DemandEventDto EventDto(Guid id) =>
        new(id, "Погодна акція", "promo", "stores", null, new List<Guid>(),
            "2026-09-12", "2026-09-19", false, null, new List<EventCoefficientDto>());

    private static DiscountDto DiscountDto(Guid id, Guid productId, Guid storeId) =>
        new(id, _tenant, productId, storeId, null, 15m, null, null, "promo",
            DateTime.UtcNow, DateTime.UtcNow.AddDays(7), "pending", false, _user, null, DateTime.UtcNow, null, null);

    [Fact]
    public async Task Apply_CreatesEventWithProductCoefficients_AndApprovedDiscountPerStore()
    {
        var p1 = Guid.NewGuid();
        var s1 = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var discountId = Guid.NewGuid();

        _repo.GetItemsAsync(_tenant, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WeatherPromoItemRow(p1, "Морозиво", 50m) });
        _events.CreateAsync(_tenant, _user, Arg.Any<UpsertEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(((DemandEventDto?)EventDto(eventId), (string?)null));
        _events.AddCoefficientAsync(eventId, Arg.Any<CreateCoefficientRequest>(), Arg.Any<CancellationToken>())
            .Returns(((EventCoefficientDto?)new EventCoefficientDto(Guid.NewGuid(), "product", p1, 1.15m, "manual"), (string?)null));
        _discounts.CreateAsync(_tenant, _user, Arg.Any<CreateDiscountRequest>(), Arg.Any<CancellationToken>())
            .Returns(((DiscountDto?)DiscountDto(discountId, p1, s1), (string?)null));
        _discounts.ApproveAsync(_tenant, discountId, _user, Arg.Any<CancellationToken>())
            .Returns(((DiscountDto?)DiscountDto(discountId, p1, s1), (string?)null));

        var (result, error) = await _sut.ApplySuggestionAsync(_tenant, _user, ApplyRequest(new[] { s1 }, new[] { p1 }));

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal(eventId, result!.EventId);
        Assert.Equal(new[] { discountId }, result.DiscountIds);
        Assert.Empty(result.Warnings);

        await _events.Received(1).CreateAsync(_tenant, _user,
            Arg.Is<UpsertEventRequest>(r => r.EventType == "promo" && r.Scope == "stores"),
            Arg.Any<CancellationToken>());
        await _events.Received(1).AddCoefficientAsync(eventId,
            Arg.Is<CreateCoefficientRequest>(c => c.ScopeType == "product" && c.ScopeId == p1 && c.Coefficient == 1.15m),
            Arg.Any<CancellationToken>());
        await _discounts.Received(1).CreateAsync(_tenant, _user,
            Arg.Is<CreateDiscountRequest>(d => d.ProductId == p1 && d.StoreId == s1
                && d.DiscountPercent == 15m && d.Reason == DiscountReason.Promo),
            Arg.Any<CancellationToken>());
        await _discounts.Received(1).ApproveAsync(_tenant, discountId, _user, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_UnknownProductId_Warns_AndIsNotCreated()
    {
        var p1 = Guid.NewGuid();
        var pUnknown = Guid.NewGuid();
        var s1 = Guid.NewGuid();
        var discountId = Guid.NewGuid();

        _repo.GetItemsAsync(_tenant, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WeatherPromoItemRow(p1, "Вода", 12m) });
        _discounts.CreateAsync(_tenant, _user, Arg.Any<CreateDiscountRequest>(), Arg.Any<CancellationToken>())
            .Returns(((DiscountDto?)DiscountDto(discountId, p1, s1), (string?)null));
        _discounts.ApproveAsync(_tenant, discountId, _user, Arg.Any<CancellationToken>())
            .Returns(((DiscountDto?)DiscountDto(discountId, p1, s1), (string?)null));

        var (result, error) = await _sut.ApplySuggestionAsync(_tenant, _user,
            ApplyRequest(new[] { s1 }, new[] { p1, pUnknown }, createEvent: false, createDiscounts: true));

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Contains(result!.Warnings, w => w.Contains(pUnknown.ToString()));
        await _discounts.Received(1).CreateAsync(_tenant, _user, Arg.Any<CreateDiscountRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_ItemWithoutRetailPrice_StillCreatesDiscount_ButWarns()
    {
        var p1 = Guid.NewGuid();
        var s1 = Guid.NewGuid();
        var discountId = Guid.NewGuid();

        _repo.GetItemsAsync(_tenant, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WeatherPromoItemRow(p1, "Товар без ціни", null) });
        _discounts.CreateAsync(_tenant, _user, Arg.Any<CreateDiscountRequest>(), Arg.Any<CancellationToken>())
            .Returns(((DiscountDto?)DiscountDto(discountId, p1, s1), (string?)null));
        _discounts.ApproveAsync(_tenant, discountId, _user, Arg.Any<CancellationToken>())
            .Returns(((DiscountDto?)DiscountDto(discountId, p1, s1), (string?)null));

        var (result, error) = await _sut.ApplySuggestionAsync(_tenant, _user,
            ApplyRequest(new[] { s1 }, new[] { p1 }, createEvent: false, createDiscounts: true));

        Assert.Null(error);
        Assert.Single(result!.DiscountIds);
        Assert.Contains(result.Warnings, w => w.Contains("без роздрібної ціни"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(95)]
    public async Task Apply_DiscountPctOutOfRange_Rejected(int pct)
    {
        var (result, error) = await _sut.ApplySuggestionAsync(_tenant, _user,
            ApplyRequest(Array.Empty<Guid>(), new[] { Guid.NewGuid() }, pct: pct));

        Assert.Null(result);
        Assert.NotNull(error);
        await _repo.DidNotReceive().GetItemsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_StartsAfterEnds_Rejected()
    {
        var (result, error) = await _sut.ApplySuggestionAsync(_tenant, _user,
            ApplyRequest(Array.Empty<Guid>(), new[] { Guid.NewGuid() },
                starts: new DateOnly(2026, 9, 20), ends: new DateOnly(2026, 9, 10)));

        Assert.Null(result);
        Assert.Equal("StartsAt must be on or before EndsAt.", error);
    }

    [Fact]
    public async Task Apply_EmptyProductIds_Rejected()
    {
        var (result, error) = await _sut.ApplySuggestionAsync(_tenant, _user,
            ApplyRequest(Array.Empty<Guid>(), Array.Empty<Guid>()));

        Assert.Null(result);
        Assert.Equal("ProductIds must not be empty.", error);
    }

    [Fact]
    public async Task Apply_NeitherEventNorDiscounts_Rejected()
    {
        var (result, error) = await _sut.ApplySuggestionAsync(_tenant, _user,
            ApplyRequest(Array.Empty<Guid>(), new[] { Guid.NewGuid() }, createEvent: false, createDiscounts: false));

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Apply_NoStoresChosen_FansDiscountsToAllActiveLocations()
    {
        var p1 = Guid.NewGuid();
        var locA = Guid.NewGuid();
        var locB = Guid.NewGuid();

        _repo.GetItemsAsync(_tenant, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WeatherPromoItemRow(p1, "Морозиво", 50m) });
        _repo.GetActiveLocationIdsAsync(_tenant, Arg.Any<CancellationToken>())
            .Returns(new[] { locA, locB });
        _discounts.CreateAsync(_tenant, _user, Arg.Any<CreateDiscountRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((DiscountDto?)DiscountDto(Guid.NewGuid(), p1, ((CreateDiscountRequest)ci[2]!).StoreId), (string?)null));
        _discounts.ApproveAsync(_tenant, Arg.Any<Guid>(), _user, Arg.Any<CancellationToken>())
            .Returns(ci => ((DiscountDto?)DiscountDto((Guid)ci[1]!, p1, locA), (string?)null));

        var (result, error) = await _sut.ApplySuggestionAsync(_tenant, _user,
            ApplyRequest(Array.Empty<Guid>(), new[] { p1 }, createEvent: false, createDiscounts: true));

        Assert.Null(error);
        Assert.Equal(2, result!.DiscountIds.Count);
        await _discounts.Received(2).CreateAsync(_tenant, _user, Arg.Any<CreateDiscountRequest>(), Arg.Any<CancellationToken>());
    }

    // ── CancelDiscountsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CancelDiscounts_OnlyCancelsIdsThatAreThisTenantsActivePromoDiscounts()
    {
        var mine = Guid.NewGuid();
        var notMine = Guid.NewGuid();
        _repo.GetActivePromoDiscountsAsync(_tenant, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new WeatherPromoActiveDiscount(mine, "Морозиво", "Магазин", 15m, 50m, 42.5m, "2026-09-12", "2026-09-19"),
            });
        _discounts.CancelAsync(_tenant, mine, Arg.Any<CancellationToken>())
            .Returns(((DiscountDto?)DiscountDto(mine, Guid.NewGuid(), Guid.NewGuid()), (string?)null));

        var result = await _sut.CancelDiscountsAsync(_tenant, new[] { mine, notMine });

        Assert.Equal(1, result.Cancelled);
        Assert.Single(result.Warnings);
        await _discounts.Received(1).CancelAsync(_tenant, mine, Arg.Any<CancellationToken>());
        await _discounts.DidNotReceive().CancelAsync(_tenant, notMine, Arg.Any<CancellationToken>());
    }
}
