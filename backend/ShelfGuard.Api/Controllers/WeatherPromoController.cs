using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.WeatherPromo;
using ShelfGuard.Application.Features.WeatherPromo.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// TASK-711 — AI weekly weather-promo suggestions ("Analyst" agent).
/// GET  /api/ai/weather-promo/suggestions — proactive card: weather forecast + top sellers +
///       weather-demand coefficients → 1–4 one-week discount campaigns (12h cache per tenant).
/// POST /api/ai/weather-promo/apply — turn a chosen suggestion into real Discount rows +
///       a calendar DemandEvent promo.
/// </summary>
[ApiController]
[Route("api/ai/weather-promo")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
[RequireModule("inventory")]
public sealed class WeatherPromoController : ControllerBase
{
    private readonly IWeatherPromoService _service;

    public WeatherPromoController(IWeatherPromoService service) => _service = service;

    [HttpGet("suggestions")]
    [ProducesResponseType(typeof(WeatherPromoSuggestionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetSuggestions([FromQuery] bool refresh = false, CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        try
        {
            var response = await _service.GetSuggestionsAsync(tenantId.Value, refresh, ct);
            return Ok(response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = $"AI сервіс недоступний: {ex.Message}" });
        }
    }

    [HttpPost("apply")]
    [ProducesResponseType(typeof(ApplySuggestionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Apply([FromBody] ApplySuggestionRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var userId = GetUserId();
        if (userId is null) return Forbid();

        var (result, error) = await _service.ApplySuggestionAsync(tenantId.Value, userId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });

        return Ok(result);
    }

    /// <summary>Active promo discounts for the tenant — the panel's "running campaigns" list.</summary>
    [HttpGet("active-discounts")]
    [ProducesResponseType(typeof(IReadOnlyList<WeatherPromoActiveDiscountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveDiscounts(CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();
        return Ok(await _service.GetActiveDiscountsAsync(tenantId.Value, ct));
    }

    /// <summary>Cancels running promo discounts by id (only the tenant's own active promo discounts).</summary>
    [HttpPost("cancel-discounts")]
    [ProducesResponseType(typeof(CancelDiscountsResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelDiscounts([FromBody] CancelDiscountsRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();
        return Ok(await _service.CancelDiscountsAsync(tenantId.Value, request.DiscountIds ?? Array.Empty<Guid>(), ct));
    }

    private Guid? GetTenantId()
    {
        Guid.TryParse(User.FindFirstValue("tenant_id"), out var id);
        return id == Guid.Empty ? null : id;
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
