using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Фаза 3 AudienceBuilder (TASK-429, design doc §1/§7). Same gate as
/// <c>MarketingAnalyticsController</c>/<c>PriceSegmentsController</c> — role-or-capability floor
/// (<c>marketing_analytics.view</c>) AND <c>[RequireModule("marketing_analytics")]</c> (rides
/// under the SAME module key as Фаза 1/2's RFM/price-segments dashboards, not a new one — design
/// doc §0 item 9 confirms no new module/capability is needed).
///
/// Every read endpoint below is POST, not GET (design doc §1's explicit architectural decision):
/// the filter shape — a list of text/category terms, an exclusion list, two thresholds — doesn't
/// fit a query string cleanly, so this controller extends the "POST for reads" shape Фаза 1/2 only
/// used for exports to every read action here too.
/// </summary>
[ApiController]
[Route("api/marketing-analytics/audience-builder")]
[Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)]
[RequireModule("marketing_analytics")]
public sealed class AudienceBuilderController : ControllerBase
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const int DefaultCategoryLimit = 20;

    private readonly IAudienceBuilderService _service;

    public AudienceBuilderController(IAudienceBuilderService service) => _service = service;

    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<AudienceCategoryOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchCategories([FromQuery] string? search, [FromQuery] int limit, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var result = await _service.SearchCategoriesAsync(tenantId.Value, search, limit <= 0 ? DefaultCategoryLimit : limit, ct);
        return Ok(result);
    }

    // ── Own-product audience ─────────────────────────────────────────────────────────────────

    [HttpPost("overview")]
    [ProducesResponseType(typeof(AudienceOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview([FromBody] AudienceBuildRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var result = await _service.GetOverviewAsync(tenantId.Value, request, ct);
        return Ok(result);
    }

    [HttpPost("buyers")]
    [ProducesResponseType(typeof(AudienceBuyerTableDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBuyers([FromBody] AudienceBuildRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        // Design doc §9: masking decision is re-derived server-side from the caller's own
        // role/capability — same gate the export path uses — never a client-supplied flag; there
        // is no "unmask" field on this request that the client could set.
        var effective = request with { CanViewUnmaskedPii = MarketingAnalyticsAuthorization.CanExportPii(User) };
        var result = await _service.GetBuyersAsync(tenantId.Value, effective, ct);
        return Ok(result);
    }

    [HttpPost("matched-items")]
    [ProducesResponseType(typeof(MatchedItemsTableDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMatchedItems([FromBody] AudienceBuildRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var result = await _service.GetMatchedItemsAsync(tenantId.Value, request, ct);
        return Ok(result);
    }

    [HttpPost("exports/buyers")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportBuyers([FromBody] ExportAudienceBuyersRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var effective = request with { UnmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User) };
        var result = await _service.ExportBuyersAsync(tenantId.Value, userId.Value, effective, ct);
        return File(result.FileBytes, ExcelContentType, result.FileName);
    }

    // ── Competitor audience ──────────────────────────────────────────────────────────────────

    [HttpPost("competitor/overview")]
    [ProducesResponseType(typeof(CompetitorOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCompetitorOverview([FromBody] CompetitorAudienceRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var result = await _service.GetCompetitorOverviewAsync(tenantId.Value, request, ct);
        return Ok(result);
    }

    [HttpPost("competitor/buyers")]
    [ProducesResponseType(typeof(CompetitorBuyerTableDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCompetitorBuyers([FromBody] CompetitorAudienceRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var effective = request with { CanViewUnmaskedPii = MarketingAnalyticsAuthorization.CanExportPii(User) };
        var result = await _service.GetCompetitorBuyersAsync(tenantId.Value, effective, ct);
        return Ok(result);
    }

    [HttpPost("exports/competitor-buyers")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportCompetitorBuyers([FromBody] ExportCompetitorBuyersRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var effective = request with { UnmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User) };
        var result = await _service.ExportCompetitorBuyersAsync(tenantId.Value, userId.Value, effective, ct);
        return File(result.FileBytes, ExcelContentType, result.FileName);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

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
