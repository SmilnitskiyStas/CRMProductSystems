using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;
using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Фаза 2 price segments + frequency/reactivation (TASK-420, design doc §2). Same gate as
/// <c>MarketingAnalyticsController</c> — role-or-capability floor (<c>marketing_analytics.view</c>)
/// AND <c>[RequireModule("marketing_analytics")]</c> (this rides under the SAME module key as
/// Фаза 1's RFM dashboard, not a new one — design doc's file list confirms no new module key).
///
/// Filtering convention shared by every comparison/frequency GET (and their POST /explain
/// siblings, which read filters from the query string too — same shape as
/// MarketingAnalyticsController's own /explain): <c>period</c> accepts "30"/"60"/"90"/omitted-
/// with-explicit-from&amp;to ("custom"); an explicit <c>from</c>+<c>to</c> pair always wins over
/// <c>period</c> when both are present; default 30 days (competitor's own confirmed default —
/// analysis doc's every control table is demonstrated starting at the 30-day view).
/// <c>storeIds</c> is a repeated query param — omitted/empty means "all stores". All-time mode
/// has no period at all (design doc §10: a genuinely different mode, not just an unbounded
/// period).
/// </summary>
[ApiController]
[Route("api/marketing-analytics/price-segments")]
[Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)]
[RequireModule("marketing_analytics")]
public sealed class PriceSegmentsController : ControllerBase
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const int DefaultPageSize = 20;

    private readonly IPriceSegmentsService _service;

    public PriceSegmentsController(IPriceSegmentsService service) => _service = service;

    // ── Comparison mode ──────────────────────────────────────────────────────────────────────

    [HttpGet("overview")]
    [ProducesResponseType(typeof(PriceSegmentsOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        var result = await _service.GetOverviewAsync(tenantId.Value, storeIds, resolvedFrom, resolvedTo, ct);
        return Ok(result);
    }

    [HttpGet("audiences/{audience}")]
    [ProducesResponseType(typeof(PriceAudienceTableDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAudienceTable(
        PriceAudienceKey audience,
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds,
        [FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? sortBy, [FromQuery] bool sortDescending,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        var request = new PriceAudienceTableRequest(
            audience, resolvedFrom, resolvedTo, storeIds, NormalizePage(page), NormalizePageSize(pageSize), sortBy, sortDescending);
        var result = await _service.GetAudienceTableAsync(tenantId.Value, request, ct);
        return Ok(result);
    }

    [HttpPost("audiences/{audience}/explain")]
    [ProducesResponseType(typeof(ExplainPriceSegmentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ExplainAudience(
        PriceAudienceKey audience,
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        try
        {
            var result = await _service.ExplainAudienceAsync(tenantId.Value, audience, storeIds, resolvedFrom, resolvedTo, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpPost("exports/audience")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAudience([FromBody] ExportPriceAudienceRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var effective = request with { UnmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User) };
        var result = await _service.ExportAudienceAsync(tenantId.Value, userId.Value, effective, ct);
        return File(result.FileBytes, ExcelContentType, result.FileName);
    }

    // ── All-time mode ────────────────────────────────────────────────────────────────────────

    [HttpGet("all-time")]
    [ProducesResponseType(typeof(PriceSegmentsAllTimeOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllTimeOverview(
        [FromQuery] PriceSegmentKey? selectedSegment, [FromQuery] Guid[]? storeIds, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var result = await _service.GetAllTimeOverviewAsync(tenantId.Value, storeIds, selectedSegment, ct);
        return Ok(result);
    }

    [HttpGet("all-time/customers")]
    [ProducesResponseType(typeof(AllTimeCustomerTableDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllTimeCustomerTable(
        [FromQuery] PriceSegmentKey? segment, [FromQuery] Guid[]? storeIds,
        [FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? sortBy, [FromQuery] bool sortDescending,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var request = new AllTimeCustomerTableRequest(
            segment, storeIds, NormalizePage(page), NormalizePageSize(pageSize), sortBy, sortDescending);
        var result = await _service.GetAllTimeCustomerTableAsync(tenantId.Value, request, ct);
        return Ok(result);
    }

    [HttpPost("all-time/segments/{segment}/explain")]
    [ProducesResponseType(typeof(ExplainPriceSegmentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ExplainAllTimeSegment(
        PriceSegmentKey segment, [FromQuery] Guid[]? storeIds, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        try
        {
            var result = await _service.ExplainAllTimeSegmentAsync(tenantId.Value, segment, storeIds, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpPost("exports/all-time")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAllTime([FromBody] ExportAllTimeRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var effective = request with { UnmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User) };
        var result = await _service.ExportAllTimeAsync(tenantId.Value, userId.Value, effective, ct);
        return File(result.FileBytes, ExcelContentType, result.FileName);
    }

    // ── Frequency & reactivation mode ────────────────────────────────────────────────────────

    [HttpGet("frequency/overview")]
    [ProducesResponseType(typeof(FrequencyOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFrequencyOverview(
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds, [FromQuery] decimal? declineThresholdPercent, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        var result = await _service.GetFrequencyOverviewAsync(
            tenantId.Value, storeIds, resolvedFrom, resolvedTo, declineThresholdPercent, ct);
        return Ok(result);
    }

    [HttpGet("frequency/audiences/{audience}")]
    [ProducesResponseType(typeof(FrequencyAudienceTableDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFrequencyAudienceTable(
        FrequencyAudienceKey audience,
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds, [FromQuery] decimal? declineThresholdPercent,
        [FromQuery] decimal? minSpend, [FromQuery] decimal? maxSpend, [FromQuery] PriceSegmentKey? priceSegment,
        [FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? sortBy, [FromQuery] bool sortDescending,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        var request = new FrequencyAudienceTableRequest(
            audience, resolvedFrom, resolvedTo, storeIds, declineThresholdPercent, minSpend, maxSpend, priceSegment,
            NormalizePage(page), NormalizePageSize(pageSize), sortBy, sortDescending);
        var result = await _service.GetFrequencyAudienceTableAsync(tenantId.Value, request, ct);
        return Ok(result);
    }

    [HttpPost("frequency/audiences/{audience}/explain")]
    [ProducesResponseType(typeof(ExplainPriceSegmentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ExplainFrequencyAudience(
        FrequencyAudienceKey audience,
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid[]? storeIds, [FromQuery] decimal? declineThresholdPercent, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (resolvedFrom, resolvedTo) = ResolvePeriod(period, from, to);
        try
        {
            var result = await _service.ExplainFrequencyAudienceAsync(
                tenantId.Value, audience, storeIds, resolvedFrom, resolvedTo, declineThresholdPercent, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpPost("exports/frequency-audience")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportFrequencyAudience([FromBody] ExportFrequencyAudienceRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var effective = request with { UnmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User) };
        var result = await _service.ExportFrequencyAudienceAsync(tenantId.Value, userId.Value, effective, ct);
        return File(result.FileBytes, ExcelContentType, result.FileName);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Explicit from+to always wins. Otherwise "30"/"60"/"90" (also bare numbers) map
    /// to a rolling window ending today; anything else (including omitted — "custom" with no
    /// explicit dates falls back here too) defaults to 30 days — the competitor's own confirmed
    /// default (analysis doc's every comparison-mode example starts at the 30-day view).</summary>
    private static (DateOnly From, DateOnly To) ResolvePeriod(string? period, DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue)
            return (from.Value, to.Value);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var days = period?.Trim() switch
        {
            "60" => 60,
            "90" => 90,
            _ => 30,
        };
        return (today.AddDays(-(days - 1)), today);
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizePageSize(int pageSize) => pageSize < 1 ? DefaultPageSize : pageSize;

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
