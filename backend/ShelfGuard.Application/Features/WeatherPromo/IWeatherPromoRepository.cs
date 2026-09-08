using ShelfGuard.Application.Features.WeatherPromo.Dtos;

namespace ShelfGuard.Application.Features.WeatherPromo;

/// <summary>
/// DB reads for the weather-promo feature. Application can't reference <c>AppDbContext</c>
/// (it only references Domain), so the cross-table context assembly the plan describes runs
/// here, in Infrastructure. RLS scopes every query to the calling tenant.
/// </summary>
public interface IWeatherPromoRepository
{
    /// <summary>
    /// All the DB-sourced context for a suggestions run: forecast locations, top sellers
    /// (last 45 days, anomalies excluded, top 30 by quantity), the tenant's weather-demand
    /// coefficients (with category/segment names resolved) and the names of promos / discounts
    /// already running (so the AI doesn't duplicate them).
    /// </summary>
    Task<WeatherPromoContextData> GetContextDataAsync(Guid tenantId, DateOnly today, CancellationToken ct = default);

    /// <summary>Tenant items by id — for the apply path (name + retail price).</summary>
    Task<IReadOnlyList<WeatherPromoItemRow>> GetItemsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);

    /// <summary>Ids of every active tenant location — discount fan-out when the caller picks no stores.</summary>
    Task<IReadOnlyList<Guid>> GetActiveLocationIdsAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Currently-active <c>promo</c> discounts for the tenant, product + store names resolved.
    /// The weather-promo panel shows these so a manager can review / cancel campaigns it created,
    /// without depending on the <c>mobile_app</c>-gated "Акційні товари" screen.
    /// </summary>
    Task<IReadOnlyList<WeatherPromoActiveDiscount>> GetActivePromoDiscountsAsync(
        Guid tenantId, CancellationToken ct = default);
}

public sealed record WeatherPromoContextData(
    IReadOnlyList<WeatherPromoForecastLocation> ForecastLocations,
    IReadOnlyList<WeatherPromoTopSellerLine> TopSellers,
    IReadOnlyList<WeatherPromoCoefficientLine> WeatherCoefficients,
    IReadOnlyList<string> ActivePromos,
    IReadOnlyList<string> ActivelyDiscountedProducts);

public sealed record WeatherPromoForecastLocation(Guid Id, string Name);

public sealed record WeatherPromoItemRow(Guid Id, string Name, decimal? PriceRetail);

public sealed record WeatherPromoActiveDiscount(
    Guid Id,
    string ProductName,
    string StoreName,
    decimal DiscountPercent,
    decimal? PriceOriginal,
    decimal? PriceDiscounted,
    string ValidFrom,
    string? ValidUntil);
