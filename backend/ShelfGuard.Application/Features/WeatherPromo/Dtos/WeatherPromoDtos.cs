namespace ShelfGuard.Application.Features.WeatherPromo.Dtos;

// ── Advisor input (assembled by WeatherPromoService, serialized to JSON in the user prompt) ──

/// <summary>Everything the "Analyst" AI agent needs to propose weekly weather-driven promos.</summary>
public sealed record WeatherPromoContext(
    DateOnly Today,
    IReadOnlyList<WeatherPromoForecastLine> Forecast,
    IReadOnlyList<WeatherPromoTopSellerLine> TopSellers,
    IReadOnlyList<WeatherPromoCoefficientLine> WeatherCoefficients,
    IReadOnlyList<string> ActivePromos,
    IReadOnlyList<string> ActivelyDiscountedProducts);

public sealed record WeatherPromoForecastLine(
    string Location,
    string Date,
    decimal? TempMin,
    decimal? TempMax,
    decimal? Precipitation,
    int? WeatherCode,
    bool IsForecast);

public sealed record WeatherPromoTopSellerLine(
    Guid ItemId,
    string Name,
    string? Category,
    decimal? PriceRetail,
    decimal QtySoldLast45Days);

public sealed record WeatherPromoCoefficientLine(
    string? Category,
    string? Segment,
    decimal? TempAbove,
    decimal? TempBelow,
    int? WeatherCode,
    decimal Coefficient);

// ── Advisor output ──────────────────────────────────────────────────────────

public sealed record WeatherPromoAdviceResult(
    IReadOnlyList<WeatherPromoSuggestion> Suggestions,
    string Model,
    int TokensUsed);

public sealed record WeatherPromoSuggestion(
    string Title,
    DateOnly StartsAt,
    DateOnly EndsAt,
    string WeatherSummary,
    string Rationale,
    decimal RecommendedDiscountPct,
    string Confidence,
    IReadOnlyList<WeatherPromoProduct> Products);

public sealed record WeatherPromoProduct(
    Guid ItemId,
    string Name,
    decimal? CurrentPrice,
    string Reason);

// ── API contract ────────────────────────────────────────────────────────────

/// <param name="Status">
/// ok — fresh or cached suggestions returned.
/// not_configured — the tenant has no Analyst AI agent.
/// not_generated — no cached result yet and the caller did not ask for a fresh run
///   (an AI call only happens on <c>?refresh=true</c>; the UI shows a "generate" button).
/// insufficient_data — no weather forecast and no recent sales to reason from.
/// </param>
public sealed record WeatherPromoSuggestionsResponse(
    string Status,
    IReadOnlyList<WeatherPromoSuggestion> Suggestions,
    DateTime? GeneratedAt,
    string? Model);

public sealed record ApplySuggestionRequest(
    string Title,
    DateOnly StartsAt,
    DateOnly EndsAt,
    decimal DiscountPct,
    IReadOnlyList<Guid> StoreIds,
    IReadOnlyList<Guid> ProductIds,
    bool CreateCalendarEvent,
    bool CreateDiscounts);

public sealed record ApplySuggestionResult(
    Guid? EventId,
    IReadOnlyList<Guid> DiscountIds,
    IReadOnlyList<string> Warnings);

/// <summary>Active <c>promo</c> discounts the panel lists so a manager can cancel a running campaign.</summary>
public sealed record WeatherPromoActiveDiscountDto(
    Guid Id,
    string ProductName,
    string StoreName,
    decimal DiscountPercent,
    decimal? PriceOriginal,
    decimal? PriceDiscounted,
    string ValidFrom,
    string? ValidUntil);

public sealed record CancelDiscountsRequest(IReadOnlyList<Guid> DiscountIds);

public sealed record CancelDiscountsResult(int Cancelled, IReadOnlyList<string> Warnings);
