using ShelfGuard.Application.Features.WeatherPromo.Dtos;

namespace ShelfGuard.Application.Features.WeatherPromo;

/// <summary>
/// AI-backed weekly weather-promo advisor (TASK-711). The "Analyst" slot looks at the upcoming
/// weather forecast, the last-45-days top sellers and the tenant's weather-demand coefficients
/// and proposes 1–4 one-week discount campaigns.
///
/// ADR-015: this interface lives in Application; the implementation
/// (<c>ShelfGuard.Infrastructure.AI.WeatherPromoAdvisor.WeatherPromoAdvisor</c>) keeps the
/// prompt template and the provider details isolated. Context assembly is done by
/// <see cref="WeatherPromoService"/> and handed over as <see cref="WeatherPromoContext"/> —
/// the advisor never touches the database.
/// </summary>
public interface IWeatherPromoAdvisor
{
    /// <summary>True when the Analyst AI slot has a usable provider config (or env fallback).</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    Task<WeatherPromoAdviceResult> RecommendAsync(WeatherPromoContext ctx, CancellationToken ct = default);
}
