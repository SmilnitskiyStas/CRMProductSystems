using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Analytics;
using ShelfGuard.Application.Features.Analytics.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize(Policy = AppPolicies.CanViewAnalytics)]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analytics;

    public AnalyticsController(IAnalyticsService analytics) => _analytics = analytics;

    [HttpGet("expiry-summary")]
    [ProducesResponseType(typeof(ExpirySummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExpirySummary(
        [FromQuery] Guid? store_id,
        [FromQuery] bool network = false,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var result = await _analytics.GetExpirySummaryAsync(tenantId, store_id, network, ct);
        return Ok(result);
    }

    [HttpGet("write-offs")]
    [ProducesResponseType(typeof(WriteOffAnalyticsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWriteOffAnalytics(
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var result = await _analytics.GetWriteOffAnalyticsAsync(tenantId, store_id, from, to, ct);
        return Ok(result);
    }

    [HttpGet("movements")]
    [ProducesResponseType(typeof(MovementAnalyticsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovementAnalytics(
        [FromQuery] string? type,
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var result = await _analytics.GetMovementAnalyticsAsync(tenantId, store_id, type, from, to, ct);
        return Ok(result);
    }

    [HttpGet("by-zone")]
    [ProducesResponseType(typeof(IReadOnlyList<ZoneAnalyticsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByZone(
        [FromQuery] Guid? store_id,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var result = await _analytics.GetByZoneAsync(tenantId, store_id, ct);
        return Ok(result);
    }

    [HttpGet("by-category")]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryAnalyticsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByCategory(
        [FromQuery] Guid? store_id,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var result = await _analytics.GetByCategoryAsync(tenantId, store_id, ct);
        return Ok(result);
    }

    [HttpGet("losses")]
    [ProducesResponseType(typeof(LossesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLosses(
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var result = await _analytics.GetLossesAsync(tenantId, store_id, from, to, ct);
        return Ok(result);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    // Returns the tenant_id from JWT claim, or null for provider (cross-tenant access).
    private Guid? ResolveTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(tenantIdStr, out var id) && id != Guid.Empty ? id : null;
    }

    private bool IsProvider() =>
        User.FindFirstValue(ClaimTypes.Role) == "provider";
}
