using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;
using ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Фаза 4 post-campaign audience analysis (TASK-472, `docs/uployal/AUDIENCE_ANALYSIS.md`). Same
/// gate as <c>MarketingAnalyticsController</c>/<c>AudienceBuilderController</c>/
/// <c>PriceSegmentsController</c> — role-or-capability floor (<c>marketing_analytics.view</c>)
/// AND <c>[RequireModule("marketing_analytics")]</c> (rides under the SAME module key as the rest
/// of Фаза 1-3, not a new one).
///
/// Unlike Фаза 1-3, most GETs here are scoped to a specific <c>segmentId</c> that can genuinely
/// not exist / not belong to this tenant / not be analyzed yet — <see cref="MapError"/> maps a
/// service error ending in "not found." to 404, anything else (e.g. "not analyzed yet") to 400,
/// same convention <c>DailySalesController.ImportCsv</c> already established.
///
/// <c>storeIds</c> is a repeated query param on every report GET (omitted/empty = all stores),
/// same shape as Фаза 1/2/3's own filters — a <c>PostCampaignSegment</c> isn't itself store-scoped
/// (its members came from an externally-sourced list, not a store-scoped query), so this is purely
/// an analysis-time filter over which stores' transactions count toward the before/after metrics.
///
/// TASK-477 (security review TASK-474, finding B): <see cref="Import"/> additionally requires
/// <see cref="MarketingAnalyticsAuthorization.CanImportSegments"/> — narrower than the class-level
/// view floor every other action here shares, since Import is a mutating, resource-costlier
/// action than the read-only report tabs (see finding A and that method's own doc comment).
/// </summary>
[ApiController]
[Route("api/marketing-analytics/post-campaign")]
[Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)]
[RequireModule("marketing_analytics")]
public sealed class PostCampaignController : ControllerBase
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const long MaxImportFileSizeBytes = 10 * 1024 * 1024; // 10 MB — see task log for rationale
    private static readonly string[] AllowedImportExtensions = [".csv", ".xlsx", ".txt"];

    private readonly IPostCampaignService _service;

    public PostCampaignController(IPostCampaignService service) => _service = service;

    // ── List ─────────────────────────────────────────────────────────────────────────────────

    [HttpGet("segments")]
    [ProducesResponseType(typeof(IReadOnlyList<PostCampaignSegmentListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSegments(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        return Ok(await _service.ListSegmentsAsync(tenantId.Value, ct));
    }

    // ── Import ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Always multipart/form-data, even for the raw-text mode (text travels as the
    /// <c>rawText</c> form field) — a deliberate, documented deviation from this controller
    /// family's usual <c>[FromBody]</c> JSON convention, needed because the SAME endpoint must
    /// also accept a binary file. Exactly one of <c>file</c>/<c>rawText</c> must be present.
    /// <c>columnIndex</c> lets the caller re-submit the SAME file with an explicit column after
    /// reviewing the auto-detected preview (brief's "single call that auto-picks, with the
    /// preview echoed back for transparency" option — see task log for why this was chosen over
    /// a two-phase stateful upload).
    /// </summary>
    [HttpPost("segments/import")]
    [RequestSizeLimit(MaxImportFileSizeBytes)]
    [ProducesResponseType(typeof(PostCampaignImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Import(
        [FromForm] IFormFile? file,
        [FromForm] string? rawText,
        [FromForm] string? name,
        [FromForm] int? columnIndex,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        // TASK-477 (security review TASK-474, finding B): Import is narrower than this
        // controller's class-level marketing_analytics.view floor — see
        // MarketingAnalyticsAuthorization.CanImportSegments's doc comment for why.
        if (!MarketingAnalyticsAuthorization.CanImportSegments(User))
            return Forbid();

        var hasFile = file is not null && file.Length > 0;
        var hasText = !string.IsNullOrWhiteSpace(rawText);

        if (hasFile == hasText) // both or neither
            return BadRequest(new { error = "Provide exactly one of a file or rawText." });

        byte[]? fileBytes = null;
        if (hasFile)
        {
            if (file!.Length > MaxImportFileSizeBytes)
                return BadRequest(new { error = $"Файл завеликий. Максимум {MaxImportFileSizeBytes / (1024 * 1024)} МБ." });

            var extension = Path.GetExtension(file.FileName);
            if (!AllowedImportExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return BadRequest(new { error = "Непідтримуваний формат файлу. Дозволено: .csv, .xlsx, .txt." });

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            fileBytes = memory.ToArray();
        }

        var request = new PostCampaignImportRequest(rawText, fileBytes, file?.FileName, name, columnIndex);
        var (result, error) = await _service.ImportAsync(tenantId.Value, userId.Value, request, ct);
        return error is not null ? BadRequest(new { error }) : Ok(result);
    }

    // ── Analyze ──────────────────────────────────────────────────────────────────────────────

    [HttpPost("segments/{id:guid}/analyze")]
    [ProducesResponseType(typeof(PostCampaignAnalyzeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Analyze(Guid id, [FromBody] PostCampaignAnalyzeRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (result, error) = await _service.AnalyzeAsync(tenantId.Value, id, request.AfterStart, request.AfterEnd, ct);
        return error is not null ? MapError(error) : Ok(result);
    }

    // ── Report tabs ──────────────────────────────────────────────────────────────────────────

    [HttpGet("segments/{id:guid}/summary")]
    [ProducesResponseType(typeof(PostCampaignSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary(Guid id, [FromQuery] Guid[]? storeIds, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (result, error) = await _service.GetSummaryAsync(tenantId.Value, id, storeIds, ct);
        return error is not null ? MapError(error) : Ok(result);
    }

    [HttpGet("segments/{id:guid}/daily-turnover")]
    [ProducesResponseType(typeof(PostCampaignDailyTurnoverDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDailyTurnover(Guid id, [FromQuery] Guid[]? storeIds, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (result, error) = await _service.GetDailyTurnoverAsync(tenantId.Value, id, storeIds, ct);
        return error is not null ? MapError(error) : Ok(result);
    }

    [HttpGet("segments/{id:guid}/rfm-activity")]
    [ProducesResponseType(typeof(PostCampaignRfmActivityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRfmActivity(Guid id, [FromQuery] Guid[]? storeIds, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (result, error) = await _service.GetRfmActivityAsync(tenantId.Value, id, storeIds, ct);
        return error is not null ? MapError(error) : Ok(result);
    }

    [HttpGet("segments/{id:guid}/customers")]
    [ProducesResponseType(typeof(PostCampaignCustomerTableDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomers(
        Guid id, [FromQuery] Guid[]? storeIds, [FromQuery] int page, [FromQuery] int pageSize,
        [FromQuery] string? sortBy, [FromQuery] bool sortDescending, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var canViewUnmaskedPii = MarketingAnalyticsAuthorization.CanExportPii(User);
        var (result, error) = await _service.GetCustomersAsync(
            tenantId.Value, id, storeIds, page, pageSize, sortBy, sortDescending, canViewUnmaskedPii, ct);
        return error is not null ? MapError(error) : Ok(result);
    }

    [HttpGet("segments/{id:guid}/migration")]
    [ProducesResponseType(typeof(PostCampaignMigrationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMigration(Guid id, [FromQuery] Guid[]? storeIds, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (result, error) = await _service.GetMigrationAsync(tenantId.Value, id, storeIds, ct);
        return error is not null ? MapError(error) : Ok(result);
    }

    // ── Explain (AI) ─────────────────────────────────────────────────────────────────────────

    /// <summary>503 when no Claude key is configured — same failure shape as
    /// <c>MarketingAnalyticsController.Explain</c>/<c>PriceSegmentsController.ExplainAudience</c>.</summary>
    [HttpPost("segments/{id:guid}/explain")]
    [ProducesResponseType(typeof(ExplainPostCampaignResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Explain(Guid id, [FromQuery] Guid[]? storeIds, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        try
        {
            var (result, error) = await _service.ExplainAsync(tenantId.Value, id, storeIds, ct);
            return error is not null ? MapError(error) : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    // ── Exports ──────────────────────────────────────────────────────────────────────────────

    [HttpPost("segments/{id:guid}/exports/customers")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportCustomers(
        Guid id, [FromBody] ExportPostCampaignCustomersRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var unmaskPii = request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User);
        var (result, error) = await _service.ExportCustomersAsync(tenantId.Value, userId.Value, id, request.StoreIds, unmaskPii, ct);
        return error is not null ? MapError(error) : File(result!.FileBytes, ExcelContentType, result.FileName);
    }

    /// <summary>
    /// TASK-478 fix (bug writeup: bug-task476-unknown-tokens-export-capped-at-20): also sets
    /// <c>X-Total-Unknown-Count</c>/<c>X-Total-Invalid-Count</c>/<c>X-Sample-Truncated</c> response
    /// headers — the response body itself is a raw file, so headers are the only channel available
    /// to tell a caller the download is a capped sample, not the complete list (directly answers
    /// this bug's own repro finding: "no X-Total-Rows/X-Truncated/any other signal"). Not yet in
    /// <c>Program.cs</c>'s CORS <c>ExposedHeaders</c>, so browser JS can't read these cross-origin
    /// today — a same-origin/server/curl caller can. The frontend's own truncation copy
    /// (<c>ValidationSummary.tsx</c>) is deliberately self-sufficient instead: it derives the same
    /// signal from <c>PostCampaignImportResultDto</c> fields it already has right after import,
    /// without depending on these headers.
    /// </summary>
    [HttpPost("segments/{id:guid}/exports/unknown-tokens")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportUnknownTokens(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var (result, error) = await _service.ExportUnknownTokensAsync(tenantId.Value, userId.Value, id, ct);
        if (error is not null) return MapError(error);

        Response.Headers["X-Total-Unknown-Count"] = result!.TotalUnknownCount.ToString();
        Response.Headers["X-Total-Invalid-Count"] = result.TotalInvalidCount.ToString();
        Response.Headers["X-Sample-Truncated"] = result.Truncated ? "true" : "false";

        return File(result.FileBytes, ExcelContentType, result.FileName);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private IActionResult MapError(string error) =>
        error.EndsWith("not found.", StringComparison.Ordinal) ? NotFound(new { error }) : BadRequest(new { error });

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
