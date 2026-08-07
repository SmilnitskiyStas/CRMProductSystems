using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Analytics;
using ShelfGuard.Application.Features.Analytics.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

// ADR-020 (TASK-346): every action in this controller is a GET behind the same gate, so the
// capability OR is applied once at the class level rather than duplicated onto ~9 identical
// per-action attributes — functionally identical to decorating every method individually.
[ApiController]
[Route("api/analytics")]
[Authorize(Policy = AppPolicies.AnalyticsViewOrCapability)]
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

    // TASK-336: current live Safe/Warning/Critical/Expired vs. a persisted snapshot
    // from `compareWeeksAgo` weeks back (dashboard status cards).
    [HttpGet("expiry-summary/compare")]
    [ProducesResponseType(typeof(ExpirySummaryComparisonDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExpirySummaryComparison(
        [FromQuery] Guid? storeId,
        [FromQuery] int compareWeeksAgo = 1,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        if (compareWeeksAgo < 1) compareWeeksAgo = 1;

        var result = await _analytics.GetExpirySummaryComparisonAsync(tenantId, storeId, compareWeeksAgo, ct);
        return Ok(result);
    }

    // TASK-336: dashboard week-over-week KPI (sales count / revenue / write-off loss).
    [HttpGet("dashboard/weekly-kpi")]
    [ProducesResponseType(typeof(WeeklyKpiDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWeeklyKpi(
        [FromQuery] Guid? store_id,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var result = await _analytics.GetWeeklyKpiAsync(tenantId, store_id, ct);
        return Ok(result);
    }

    [HttpGet("write-offs")]
    [ProducesResponseType(typeof(WriteOffAnalyticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WriteOffsComparisonDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWriteOffAnalytics(
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool compare = false,
        [FromQuery] DateOnly? compareFrom = null,
        [FromQuery] DateOnly? compareTo = null,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        // Backward-compatible: unwrapped shape unless comparison is explicitly requested.
        if (!compare)
        {
            var result = await _analytics.GetWriteOffAnalyticsAsync(tenantId, store_id, from, to, ct);
            return Ok(result);
        }

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);
        var (cFrom, cTo) = ResolveCompareRange(resolvedFrom, resolvedTo, compareFrom, compareTo);
        var comparison = await _analytics.GetWriteOffAnalyticsComparisonAsync(tenantId, store_id, resolvedFrom, resolvedTo, cFrom, cTo, ct);
        return Ok(comparison);
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

    // TASK-481: category drill-down — products within one category (or the "uncategorized"
    // bucket when category_id is omitted), stock rollup + sales rollup + margin (ADR-027).
    [HttpGet("by-category/products")]
    [ProducesResponseType(typeof(CategoryProductBreakdownDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByCategoryProducts(
        [FromQuery] Guid? category_id,
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);
        var includeMargin = AnalyticsAuthorization.CanViewMargin(User);

        var result = await _analytics.GetCategoryProductBreakdownAsync(
            tenantId, store_id, category_id, resolvedFrom, resolvedTo, includeMargin, ct);
        return Ok(result);
    }

    [HttpGet("losses")]
    [ProducesResponseType(typeof(LossesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LossesComparisonDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLosses(
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool compare = false,
        [FromQuery] DateOnly? compareFrom = null,
        [FromQuery] DateOnly? compareTo = null,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        if (!compare)
        {
            var result = await _analytics.GetLossesAsync(tenantId, store_id, from, to, ct);
            return Ok(result);
        }

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);
        var (cFrom, cTo) = ResolveCompareRange(resolvedFrom, resolvedTo, compareFrom, compareTo);
        var comparison = await _analytics.GetLossesComparisonAsync(tenantId, store_id, resolvedFrom, resolvedTo, cFrom, cTo, ct);
        return Ok(comparison);
    }

    // TASK-481: losses drill-down by product — a single endpoint serves both the by-store and
    // by-reason drill-downs via independent optional AND-filters. No margin gate: LossAmount is
    // already shown in aggregate to every store_manager+ caller today (ADR-027 §1).
    [HttpGet("losses/by-product")]
    [ProducesResponseType(typeof(LossesByProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLossesByProduct(
        [FromQuery] Guid? store_id,
        [FromQuery] string? reason,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);

        var result = await _analytics.GetLossesByProductAsync(tenantId, store_id, reason, resolvedFrom, resolvedTo, ct);
        return Ok(result);
    }

    // TASK-489: losses/write-offs trend over time — mirrors pos/revenue-trend's shape (same
    // store_id/from/to/group_by params and day|week values). No compare-mode variant (not asked
    // for in this follow-up batch). No margin gate: same reasoning as losses/by-product above —
    // LossAmount is already shown in aggregate to every store_manager+ caller today (ADR-027 §1).
    [HttpGet("losses/trend")]
    [ProducesResponseType(typeof(LossesTrendDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLossesTrend(
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string group_by = "day",
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);

        var result = await _analytics.GetLossesTrendAsync(tenantId, store_id, resolvedFrom, resolvedTo, group_by, ct);
        return Ok(result);
    }

    // ── POS analytics ─────────────────────────────────────────────────────

    [HttpGet("pos/summary")]
    [ProducesResponseType(typeof(PosAnalyticsSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PosSummaryComparisonDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPosSummary(
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool compare = false,
        [FromQuery] DateOnly? compareFrom = null,
        [FromQuery] DateOnly? compareTo = null,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);

        if (!compare)
        {
            var result = await _analytics.GetPosSummaryAsync(tenantId, store_id, resolvedFrom, resolvedTo, ct);
            return Ok(result);
        }

        var (cFrom, cTo) = ResolveCompareRange(resolvedFrom, resolvedTo, compareFrom, compareTo);
        var comparison = await _analytics.GetPosSummaryComparisonAsync(tenantId, store_id, resolvedFrom, resolvedTo, cFrom, cTo, ct);
        return Ok(comparison);
    }

    [HttpGet("pos/revenue-trend")]
    [ProducesResponseType(typeof(PosRevenueTrendDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PosRevenueTrendComparisonDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPosRevenueTrend(
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string group_by = "day",
        [FromQuery] bool compare = false,
        [FromQuery] DateOnly? compareFrom = null,
        [FromQuery] DateOnly? compareTo = null,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);

        if (!compare)
        {
            var result = await _analytics.GetPosRevenueTrendAsync(tenantId, store_id, resolvedFrom, resolvedTo, group_by, ct);
            return Ok(result);
        }

        var (cFrom, cTo) = ResolveCompareRange(resolvedFrom, resolvedTo, compareFrom, compareTo);
        var comparison = await _analytics.GetPosRevenueTrendComparisonAsync(tenantId, store_id, resolvedFrom, resolvedTo, group_by, cFrom, cTo, ct);
        return Ok(comparison);
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

    // TASK-490: dead-stock / worst-performing-products view -- NOT pos/top-products sorted
    // ascending. That query groups PosTransactionItems, so a product with zero sales in the
    // period never appears in the result at all (nothing to group). This starts from the
    // catalog/stock side instead (active items with on-hand stock) and LEFT-JOINs the sales
    // rollup, COALESCEing missing sales to 0 -- see GetWorstProductsAsync's own comments for the
    // query shape. No margin gate: same sensitivity class as pos/top-products above (already
    // ungated for store_manager+).
    [HttpGet("pos/worst-products")]
    [ProducesResponseType(typeof(WorstProductsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorstProducts(
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
        var result = await _analytics.GetWorstProductsAsync(tenantId, store_id, resolvedFrom, resolvedTo, limit, ct);
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

    // TASK-482: single-product sales trend — row-click drill-down from pos/top-products. No
    // compare-mode variant (a row-click drill-down isn't a page-level KPI trend concept, unlike
    // pos/revenue-trend above). 404s when productId doesn't resolve to a real Item in the
    // caller's tenant scope (mirrors ItemsController.GetById's nullable-DTO -> NotFound() convention).
    [HttpGet("pos/products/{productId:guid}/trend")]
    [ProducesResponseType(typeof(ProductSalesTrendDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductSalesTrend(
        Guid productId,
        [FromQuery] Guid? store_id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string group_by = "day",
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null && !IsProvider()) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolveDateRange(from, to);
        var includeMargin = AnalyticsAuthorization.CanViewMargin(User);

        var result = await _analytics.GetProductSalesTrendAsync(
            tenantId, store_id, productId, resolvedFrom, resolvedTo, group_by, includeMargin, ct);

        return result is null ? NotFound() : Ok(result);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static (DateOnly From, DateOnly To) ResolveDateRange(DateOnly? from, DateOnly? to)
    {
        var resolvedTo   = to   ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var resolvedFrom = from ?? resolvedTo.AddDays(-30);
        return (resolvedFrom, resolvedTo);
    }

    // TASK-336: when the caller doesn't pass explicit compareFrom/compareTo, default to the
    // same-length period immediately preceding `from` (e.g. from=Jul1..to=Jul7 -> compare
    // Jun24..Jun30).
    private static (DateOnly From, DateOnly To) ResolveCompareRange(
        DateOnly from, DateOnly to, DateOnly? compareFrom, DateOnly? compareTo)
    {
        if (compareFrom.HasValue && compareTo.HasValue)
            return (compareFrom.Value, compareTo.Value);

        var lengthDays     = to.DayNumber - from.DayNumber;
        var resolvedTo     = from.AddDays(-1);
        var resolvedFrom   = from.AddDays(-lengthDays - 1);
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
