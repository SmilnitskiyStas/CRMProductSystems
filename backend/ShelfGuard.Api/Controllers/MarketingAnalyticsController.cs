using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MarketingAnalytics;
using ShelfGuard.Application.Features.MarketingAnalytics.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// RFM marketing-analytics dashboard (TASK-406, plan §"Фаза 1"). Gated by both a base role-or-
/// capability floor (`MarketingAnalyticsViewOrCapability` — CanViewAnalyticsRoles OR the
/// `marketing_analytics.view` TenantRole capability, ADR-020, same shape as the existing
/// AnalyticsController's `AnalyticsViewOrCapability`) AND [RequireModule("marketing_analytics")]
/// — a tenant needs the module active on top of the caller clearing the role/capability floor.
///
/// TASK-414 (security review TASK-412, finding C): this used to be a bare `CanViewAnalytics`
/// role-only policy — the exact same 4-role set as `MarketingAnalyticsAuthorization.
/// CanExportPii`'s own first branch, which made the `marketing_analytics.export_pii` capability
/// dead code (nobody who lacked store_manager+ rank could ever reach the controller to exercise
/// it). Widening to the OR-capability policy is what actually lets a granted
/// `marketing_analytics.view` holder below store_manager reach every action here — the
/// export-PII decision itself is unchanged, still resolved per-request by
/// `MarketingAnalyticsAuthorization.CanExportPii` in each Export* action below.
///
/// Filtering convention shared by every read endpoint below (including the POST /explain,
/// which triggers a side-effecting Claude call but still only ever reads its filter from the
/// query string — same shape as the GETs, no request body needed for it): `period` accepts
/// "3m"/"6m"/"12m"/"all"/omitted-with-explicit-from&amp;to ("custom"); an explicit `from`+`to`
/// pair always wins over `period` when both are present. `storeIds` is a repeated query param
/// (`?storeIds=guid1&amp;storeIds=guid2`) — omitted or empty means "all stores".
/// </summary>
[ApiController]
[Route("api/marketing-analytics")]
[Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)]
[RequireModule("marketing_analytics")]
public sealed class MarketingAnalyticsController : ControllerBase
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const int DefaultCrossSellLimit = 10;
    private const int DefaultStoreMigrationCustomerLimit = 100;
    private const int MaxStoreMigrationCustomerLimit = 500;

    private readonly IMarketingAnalyticsService _service;

    public MarketingAnalyticsController(IMarketingAnalyticsService service) => _service = service;

    [HttpGet("overview")]
    [ProducesResponseType(typeof(RfmOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] string? period,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        var result = await _service.GetOverviewAsync(tenantId.Value, storeIds, resolvedFrom, resolvedTo, ct);
        return Ok(result);
    }

    [HttpGet("segments/{key}")]
    [ProducesResponseType(typeof(RfmSegmentDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSegmentDetail(
        RfmSegmentKey key,
        [FromQuery] string? period,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        var result = await _service.GetSegmentDetailAsync(tenantId.Value, storeIds, resolvedFrom, resolvedTo, key, ct);
        return Ok(result);
    }

    /// <summary>{productName} must be URL-encoded by the caller (product names are free-text,
    /// may contain spaces/apostrophes/etc — e.g. "Банан ваговий" → "%D0%91%D0%B0%D0%BD%D0%B0%D0%BD...").</summary>
    [HttpGet("segments/{key}/products/{productName}/affinity")]
    [ProducesResponseType(typeof(RfmAffinityResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAffinity(
        RfmSegmentKey key,
        string productName,
        [FromQuery] string? period,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        var result = await _service.GetAffinityAsync(
            tenantId.Value, storeIds, resolvedFrom, resolvedTo, key, productName, ClampLimit(limit), ct);
        return Ok(result);
    }

    [HttpGet("segments/{key}/products/{productName}/basket")]
    [ProducesResponseType(typeof(RfmBasketResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBasket(
        RfmSegmentKey key,
        string productName,
        [FromQuery] string? period,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        var result = await _service.GetBasketAsync(
            tenantId.Value, storeIds, resolvedFrom, resolvedTo, key, productName, ClampLimit(limit), ct);
        return Ok(result);
    }

    /// <summary>Optional Claude "Пояснити детальніше" — user-triggered only, one segment at a
    /// time. 503 when no Claude key is configured (tenant integration_configs nor env
    /// fallback) — same failure shape as AiOrdersController's Claude-unavailable case.</summary>
    [HttpPost("segments/{key}/explain")]
    [ProducesResponseType(typeof(ExplainRfmSegmentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Explain(
        RfmSegmentKey key,
        [FromQuery] string? period,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        try
        {
            var result = await _service.ExplainSegmentAsync(tenantId.Value, storeIds, resolvedFrom, resolvedTo, key, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    // ── Store migration (TASK-502) ────────────────────────────────────────────
    // Same query-param shape as every GET above. Two GETs (overview + customer drill-down) plus
    // one export — the customer drill-down endpoint isn't in the original 2-endpoint brief for
    // this task, but the repository/service layer explicitly builds
    // GetStoreMigrationCustomersAsync for "both on-screen and export" use, and the feature's own
    // goal ("drill-down customer list") requires an on-screen data source for it — added as the
    // objective completion of what the lower layers already expose. See task log for TASK-502.

    [HttpGet("store-migration")]
    [ProducesResponseType(typeof(StoreMigrationOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStoreMigration(
        [FromQuery] string? period,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        var result = await _service.GetStoreMigrationAsync(tenantId.Value, storeIds, resolvedFrom, resolvedTo, ct);
        return Ok(result);
    }

    /// <summary>On-screen drill-down list for the store-migration matrix — PII always masked
    /// (unmasking is only ever available through the audited Excel export below).</summary>
    [HttpGet("store-migration/customers")]
    [ProducesResponseType(typeof(IReadOnlyList<StoreMigrationCustomerRowDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStoreMigrationCustomers(
        [FromQuery] string? period,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        var result = await _service.GetStoreMigrationCustomersAsync(
            tenantId.Value, storeIds, resolvedFrom, resolvedTo, ClampStoreMigrationCustomerLimit(limit), ct);
        return Ok(result);
    }

    [HttpPost("exports/store-migration")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportStoreMigration([FromBody] ExportStoreMigrationRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var effective = request with { UnmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User) };
        var result = await _service.ExportStoreMigrationAsync(tenantId.Value, userId.Value, effective, ct);
        return File(result.FileBytes, ExcelContentType, result.FileName);
    }

    // ── Exports ──────────────────────────────────────────────────────────────

    [HttpPost("exports/segment")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportSegment([FromBody] ExportRfmFilterRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var effective = request with { UnmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User) };
        var result = await _service.ExportSegmentAsync(tenantId.Value, userId.Value, effective, ct);
        return File(result.FileBytes, ExcelContentType, result.FileName);
    }

    [HttpPost("exports/product-buyers")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportProductBuyers([FromBody] ExportRfmProductRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var effective = request with { UnmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User) };
        var result = await _service.ExportProductBuyersAsync(tenantId.Value, userId.Value, effective, ct);
        return File(result.FileBytes, ExcelContentType, result.FileName);
    }

    [HttpPost("exports/product-pair-buyers")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportProductPairBuyers([FromBody] ExportRfmProductPairRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var effective = request with { UnmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User) };
        var result = await _service.ExportProductPairBuyersAsync(tenantId.Value, userId.Value, effective, ct);
        return File(result.FileBytes, ExcelContentType, result.FileName);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>Explicit from+to always wins. Otherwise: "3m"/"6m"/"12m" (also bare "3"/"6"/"12")
    /// map to a rolling window ending today; "all" maps to the platform's full history;
    /// anything else (including omitted) defaults to 6 months — matches the competitor's own
    /// default window (RFM_ANALYSIS.md §4.1: "6 міс" active on first load).</summary>
    private static (DateOnly From, DateOnly To) ResolvePeriod(string? period, DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue)
            return (from.Value, to.Value);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return period?.Trim().ToLowerInvariant() switch
        {
            "3m" or "3" => (today.AddMonths(-3), today),
            "6m" or "6" => (today.AddMonths(-6), today),
            "12m" or "12" => (today.AddMonths(-12), today),
            "all" => (DateOnly.MinValue, today),
            _ => (today.AddMonths(-6), today),
        };
    }

    private static int ClampLimit(int limit) => limit is < 1 or > 50 ? DefaultCrossSellLimit : limit;

    private static int ClampStoreMigrationCustomerLimit(int limit) =>
        limit is < 1 or > MaxStoreMigrationCustomerLimit ? DefaultStoreMigrationCustomerLimit : limit;

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    private Guid? ResolveUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
