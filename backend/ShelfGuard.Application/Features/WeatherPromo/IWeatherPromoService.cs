using ShelfGuard.Application.Features.WeatherPromo.Dtos;

namespace ShelfGuard.Application.Features.WeatherPromo;

public interface IWeatherPromoService
{
    /// <summary>
    /// Weekly weather-promo suggestions for the tenant. Cached 12h per tenant; <paramref name="refresh"/>
    /// forces a fresh AI call (and rewrites the cache). Returns <c>status = not_configured</c> when the
    /// Analyst AI slot isn't set up and <c>status = insufficient_data</c> when there's neither a weather
    /// forecast nor recent sales to reason about.
    /// </summary>
    Task<WeatherPromoSuggestionsResponse> GetSuggestionsAsync(
        Guid tenantId, bool refresh, CancellationToken ct = default);

    /// <summary>
    /// Turns a chosen suggestion into real <c>Discount</c> rows (created + approved → active on the
    /// cash register) and/or a calendar <c>DemandEvent</c> promo with per-product demand coefficients.
    /// Returns a non-null error only for request-level validation failures; per-item problems come
    /// back as warnings on the result.
    /// </summary>
    Task<(ApplySuggestionResult? Result, string? Error)> ApplySuggestionAsync(
        Guid tenantId, Guid userId, ApplySuggestionRequest request, CancellationToken ct = default);

    /// <summary>Active <c>promo</c> discounts for the tenant — the panel's "running campaigns" list.</summary>
    Task<IReadOnlyList<WeatherPromoActiveDiscountDto>> GetActiveDiscountsAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>Cancels the given discounts (only ones that are this tenant's active promo discounts).</summary>
    Task<CancelDiscountsResult> CancelDiscountsAsync(
        Guid tenantId, IReadOnlyList<Guid> discountIds, CancellationToken ct = default);
}
