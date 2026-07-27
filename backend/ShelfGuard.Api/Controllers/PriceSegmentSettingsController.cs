using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;
using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Per-tenant Фаза 2 configuration (TASK-420) — one row per tenant, exact same lazy-upsert shape
/// as <see cref="LoyaltySettingsController"/> (design doc §0 file list names that controller as
/// the precedent). GET returns proposed defaults (30% decline threshold, no minimum receipts)
/// when the tenant has never saved a row, so the Settings page has something sensible to show
/// before first save.
/// </summary>
[ApiController]
[Route("api/settings/price-segments")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
public sealed class PriceSegmentSettingsController : ControllerBase
{
    private readonly IPriceSegmentsService _service;

    public PriceSegmentSettingsController(IPriceSegmentsService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(PriceSegmentSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var dto = await _service.GetSettingsAsync(tenantId.Value, ct);
        return Ok(dto);
    }

    [HttpPut]
    [ProducesResponseType(typeof(PriceSegmentSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upsert([FromBody] UpsertPriceSegmentSettingsRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (dto, error) = await _service.UpsertSettingsAsync(tenantId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });

        return Ok(dto);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
