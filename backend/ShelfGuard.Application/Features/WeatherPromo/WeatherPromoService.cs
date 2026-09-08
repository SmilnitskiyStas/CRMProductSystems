using Microsoft.Extensions.Caching.Memory;
using ShelfGuard.Application.Features.Discounts;
using ShelfGuard.Application.Features.Discounts.Dtos;
using ShelfGuard.Application.Features.Events;
using ShelfGuard.Application.Features.Events.Dtos;
using ShelfGuard.Application.Features.WeatherPromo.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.WeatherPromo;

/// <summary>
/// TASK-711 — AI weekly weather-promo suggestions. Assembles the weather + sales + coefficient
/// context, asks the isolated <see cref="IWeatherPromoAdvisor"/>, caches the answer for 12h, and
/// (on <c>apply</c>) fans a chosen suggestion out into <c>Discount</c> rows + a calendar promo.
/// Thin Application service — the AI prompt stays in Infrastructure per ADR-015.
/// </summary>
public sealed class WeatherPromoService : IWeatherPromoService
{
    private const int MaxForecastRows = 40;
    private const decimal MinDiscountPct = 1m;
    private const decimal MaxDiscountPct = 90m;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

    private readonly IWeatherPromoAdvisor _advisor;
    private readonly IWeatherPromoRepository _repo;
    private readonly IWeatherRepository _weatherRepo;
    private readonly IEventService _events;
    private readonly IDiscountService _discounts;
    private readonly IMemoryCache _cache;

    public WeatherPromoService(
        IWeatherPromoAdvisor advisor,
        IWeatherPromoRepository repo,
        IWeatherRepository weatherRepo,
        IEventService events,
        IDiscountService discounts,
        IMemoryCache cache)
    {
        _advisor = advisor;
        _repo = repo;
        _weatherRepo = weatherRepo;
        _events = events;
        _discounts = discounts;
        _cache = cache;
    }

    private sealed record CacheEntry(
        IReadOnlyList<WeatherPromoSuggestion> Suggestions, DateTime GeneratedAt, string Model);

    // ── Suggestions ─────────────────────────────────────────────────────────

    public async Task<WeatherPromoSuggestionsResponse> GetSuggestionsAsync(
        Guid tenantId, bool refresh, CancellationToken ct = default)
    {
        if (!await _advisor.IsConfiguredAsync(ct))
            return new WeatherPromoSuggestionsResponse("not_configured", Array.Empty<WeatherPromoSuggestion>(), null, null);

        var cacheKey = $"weather-promo:{tenantId}";
        if (!refresh && _cache.TryGetValue(cacheKey, out CacheEntry? cached) && cached is not null)
            return new WeatherPromoSuggestionsResponse("ok", cached.Suggestions, cached.GeneratedAt, cached.Model);

        // An AI call costs money and takes seconds — never make one on a plain read. The card
        // shows a "Згенерувати поради" button that calls back with refresh=true; after that the
        // result is cached for 12h and served automatically on subsequent visits.
        if (!refresh)
            return new WeatherPromoSuggestionsResponse("not_generated", Array.Empty<WeatherPromoSuggestion>(), null, null);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var data = await _repo.GetContextDataAsync(tenantId, today, ct);

        var forecast = new List<WeatherPromoForecastLine>();
        foreach (var loc in data.ForecastLocations)
        {
            if (forecast.Count >= MaxForecastRows) break;

            var rows = await _weatherRepo.GetRangeAsync(loc.Id, today, today.AddDays(14), ct);
            foreach (var w in rows)
            {
                if (forecast.Count >= MaxForecastRows) break;
                if (w.TempMin is null && w.TempMax is null && w.TempAvg is null) continue;

                forecast.Add(new WeatherPromoForecastLine(
                    loc.Name,
                    w.Date.ToString("yyyy-MM-dd"),
                    w.TempMin,
                    w.TempMax,
                    w.Precipitation,
                    w.WeatherCode,
                    w.IsForecast));
            }
        }

        if (forecast.Count == 0 && data.TopSellers.Count == 0)
            return new WeatherPromoSuggestionsResponse("insufficient_data", Array.Empty<WeatherPromoSuggestion>(), null, null);

        var ctx = new WeatherPromoContext(
            today,
            forecast,
            data.TopSellers,
            data.WeatherCoefficients,
            data.ActivePromos,
            data.ActivelyDiscountedProducts);

        var advice = await _advisor.RecommendAsync(ctx, ct);

        var topByItem = new Dictionary<Guid, WeatherPromoTopSellerLine>();
        foreach (var t in data.TopSellers) topByItem[t.ItemId] = t;

        var suggestions = advice.Suggestions
            .Select(s => s with
            {
                Products = s.Products
                    .Select(p => topByItem.TryGetValue(p.ItemId, out var top)
                        ? p with
                        {
                            CurrentPrice = top.PriceRetail,
                            Name = string.IsNullOrWhiteSpace(p.Name) ? top.Name : p.Name,
                        }
                        : p)
                    .ToList(),
            })
            .ToList();

        var entry = new CacheEntry(suggestions, DateTime.UtcNow, advice.Model);
        _cache.Set(cacheKey, entry, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });

        return new WeatherPromoSuggestionsResponse("ok", suggestions, entry.GeneratedAt, entry.Model);
    }

    // ── Apply ───────────────────────────────────────────────────────────────

    public async Task<(ApplySuggestionResult? Result, string? Error)> ApplySuggestionAsync(
        Guid tenantId, Guid userId, ApplySuggestionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return (null, "Title is required.");
        if (request.DiscountPct < MinDiscountPct || request.DiscountPct > MaxDiscountPct)
            return (null, $"DiscountPct must be between {MinDiscountPct} and {MaxDiscountPct}.");
        if (request.StartsAt > request.EndsAt)
            return (null, "StartsAt must be on or before EndsAt.");
        if (request.ProductIds is null || request.ProductIds.Count == 0)
            return (null, "ProductIds must not be empty.");
        if (!request.CreateCalendarEvent && !request.CreateDiscounts)
            return (null, "Enable at least one of CreateCalendarEvent or CreateDiscounts.");

        var warnings = new List<string>();

        var productIds = request.ProductIds.Distinct().ToList();
        var items = await _repo.GetItemsAsync(tenantId, productIds, ct);
        var itemById = items.ToDictionary(i => i.Id);

        var knownIds = productIds.Where(itemById.ContainsKey).ToList();
        foreach (var missing in productIds.Where(id => !itemById.ContainsKey(id)))
            warnings.Add($"Товар {missing} не знайдено в каталозі — пропущено.");

        if (knownIds.Count == 0)
            return (null, "None of the given ProductIds belong to this tenant.");

        var storeIds = (request.StoreIds ?? Array.Empty<Guid>()).Where(s => s != Guid.Empty).Distinct().ToList();

        Guid? eventId = null;
        if (request.CreateCalendarEvent)
            eventId = await CreateCalendarEventAsync(tenantId, userId, request, storeIds, knownIds, warnings, ct);

        var discountIds = new List<Guid>();
        if (request.CreateDiscounts)
            await CreateDiscountsAsync(tenantId, userId, request, storeIds, knownIds, itemById, discountIds, warnings, ct);

        return (new ApplySuggestionResult(eventId, discountIds, warnings), null);
    }

    // ── Running campaigns ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<WeatherPromoActiveDiscountDto>> GetActiveDiscountsAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var rows = await _repo.GetActivePromoDiscountsAsync(tenantId, ct);
        return rows
            .Select(d => new WeatherPromoActiveDiscountDto(
                d.Id, d.ProductName, d.StoreName, d.DiscountPercent,
                d.PriceOriginal, d.PriceDiscounted, d.ValidFrom, d.ValidUntil))
            .ToList();
    }

    public async Task<CancelDiscountsResult> CancelDiscountsAsync(
        Guid tenantId, IReadOnlyList<Guid> discountIds, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        if (discountIds is null || discountIds.Count == 0)
            return new CancelDiscountsResult(0, warnings);

        // Only cancel ids that are genuinely this tenant's active promo discounts.
        var eligible = (await _repo.GetActivePromoDiscountsAsync(tenantId, ct))
            .Select(d => d.Id)
            .ToHashSet();

        var cancelled = 0;
        foreach (var id in discountIds.Distinct())
        {
            if (!eligible.Contains(id))
            {
                warnings.Add($"Знижку {id} пропущено — вона не є активною погодною акцією цього тенанта.");
                continue;
            }

            var (_, error) = await _discounts.CancelAsync(tenantId, id, ct);
            if (error is not null) { warnings.Add($"Знижку {id} не скасовано: {error}"); continue; }
            cancelled++;
        }

        return new CancelDiscountsResult(cancelled, warnings);
    }

    private async Task<Guid?> CreateCalendarEventAsync(
        Guid tenantId,
        Guid userId,
        ApplySuggestionRequest request,
        IReadOnlyList<Guid> storeIds,
        IReadOnlyList<Guid> knownIds,
        List<string> warnings,
        CancellationToken ct)
    {
        var scope = storeIds.Count > 0 ? "stores" : "network";
        var (ev, error) = await _events.CreateAsync(tenantId, userId, new UpsertEventRequest(
            Name: request.Title,
            EventType: "promo",
            Scope: scope,
            StoreId: null,
            StoreIds: storeIds.Count > 0 ? storeIds.ToList() : null,
            StartsAt: request.StartsAt,
            EndsAt: request.EndsAt,
            IsRecurring: false,
            Notes: "Створено з AI-поради (погодна тижнева акція)"), ct);

        if (error is not null || ev is null)
        {
            warnings.Add($"Подію календаря не створено: {error ?? "невідома помилка"}.");
            return null;
        }

        var coefficient = Math.Round(1m + request.DiscountPct / 100m, 2);
        foreach (var itemId in knownIds)
        {
            var (_, coefError) = await _events.AddCoefficientAsync(ev.Id, new CreateCoefficientRequest(
                ScopeType: "product",
                ScopeId: itemId,
                Coefficient: coefficient), ct);
            if (coefError is not null)
                warnings.Add($"Коефіцієнт попиту для товару {itemId} не додано: {coefError}");
        }

        return ev.Id;
    }

    private async Task CreateDiscountsAsync(
        Guid tenantId,
        Guid userId,
        ApplySuggestionRequest request,
        IReadOnlyList<Guid> storeIds,
        IReadOnlyList<Guid> knownIds,
        IReadOnlyDictionary<Guid, WeatherPromoItemRow> itemById,
        List<Guid> discountIds,
        List<string> warnings,
        CancellationToken ct)
    {
        var targetStores = storeIds.ToList();
        if (targetStores.Count == 0)
        {
            targetStores = (await _repo.GetActiveLocationIdsAsync(tenantId, ct)).ToList();
            if (targetStores.Count == 0)
            {
                warnings.Add("Немає активних локацій — знижки не створено.");
                return;
            }
        }

        var validFrom = request.StartsAt.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var validUntil = request.EndsAt.ToDateTime(new TimeOnly(23, 59), DateTimeKind.Utc);

        foreach (var itemId in knownIds)
        {
            var item = itemById[itemId];
            if (item.PriceRetail is null)
                warnings.Add($"Товар «{item.Name}» без роздрібної ціни — знижку створено лише як відсоток.");

            foreach (var storeId in targetStores)
            {
                var (created, createError) = await _discounts.CreateAsync(tenantId, userId, new CreateDiscountRequest(
                    ProductId: itemId,
                    StoreId: storeId,
                    DiscountPercent: request.DiscountPct,
                    Reason: DiscountReason.Promo,
                    ProductStockId: null,
                    PriceOriginal: item.PriceRetail,
                    ValidFrom: validFrom,
                    ValidUntil: validUntil), ct);

                if (createError is not null || created is null)
                {
                    warnings.Add($"Знижку для «{item.Name}» не створено: {createError ?? "невідома помилка"}.");
                    continue;
                }

                var (_, approveError) = await _discounts.ApproveAsync(tenantId, created.Id, userId, ct);
                if (approveError is not null)
                    warnings.Add($"Знижку для «{item.Name}» створено, але не активовано: {approveError}");

                discountIds.Add(created.Id);
            }
        }
    }
}
