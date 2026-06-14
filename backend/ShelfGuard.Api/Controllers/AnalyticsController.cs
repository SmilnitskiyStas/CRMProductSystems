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

    // ── POS analytics ─────────────────────────────────────────────────────

    [HttpGet("pos/summary")]
    [ProducesResponseType(typeof(PosAnalyticsSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPosSummary(
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);
        var result = await _analytics.GetPosSummaryAsync(tenantId, store_id, resolvedFrom, resolvedTo, ct);
        return Ok(result);
    }

    [HttpGet("pos/revenue-trend")]
    [ProducesResponseType(typeof(PosRevenueTrendDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPosRevenueTrend(
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string group_by = "day",
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);
        var result = await _analytics.GetPosRevenueTrendAsync(tenantId, store_id, resolvedFrom, resolvedTo, group_by, ct);
        return Ok(result);
    }

    [HttpGet("pos/top-products")]
    [ProducesResponseType(typeof(PosTopProductsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPosTopProducts(
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        if (limit is < 1 or > 100) limit = 10;

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);
        var result = await _analytics.GetPosTopProductsAsync(tenantId, store_id, resolvedFrom, resolvedTo, limit, ct);
        return Ok(result);
    }

    [HttpGet("pos/cashiers")]
    [ProducesResponseType(typeof(PosCashierStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPosCashierStats(
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);
        var result = await _analytics.GetPosCashierStatsAsync(tenantId, store_id, resolvedFrom, resolvedTo, ct);
        return Ok(result);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static (DateOnly From, DateOnly To) ResolveDateRange(DateOnly? from, DateOnly? to)
    {
        var resolvedTo   = to   ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var resolvedFrom = from ?? resolvedTo.AddDays(-30);
        return (resolvedFrom, resolvedTo);
    }

    // Returns the tenant_id from JWT claim, or null for provider (cross-tenant access).
    private Guid? ResolveTenantId()
    {
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(tenantIdStr, out var id) && id != Guid.Empty ? id : null;
    }

    private bool IsProvider() =>
        User.FindFirstValue(ClaimTypes.Role) == "provider";
}
