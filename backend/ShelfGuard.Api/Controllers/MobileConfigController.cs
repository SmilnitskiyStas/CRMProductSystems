using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Serves the resolved tenant's currently PUBLISHED mobile app configuration document to the
/// consumer mobile client (CLAUDE CODE SPEC ЕТАП 4, TASK-534) — mobile's #1 blocking contract per
/// <c>docs/mobile/MOBILE_CURRENT_STATE.md</c> §8/§15/§12. First endpoint under the new
/// <c>/api/v1/</c> prefix (decision 2, TASK-556): only new consumer-platform endpoints get
/// versioned going forward — the existing unversioned <c>/api/...</c> surface is unaffected.
///
/// TENANT-TRANSPORT DECISION (this is what unblocks
/// <c>docs/mobile/MOBILE_CURRENT_STATE.md</c>'s "documented active-tenant transport for
/// GET /api/v1/mobile/config" blocking item): the route stays literally
/// <c>GET /api/v1/mobile/config</c> per MASTER SPEC §11 / the CLAUDE CODE SPEC — no tenant path
/// segment. <c>tenantId</c> travels as an explicit <c>?tenantId=</c> query parameter instead,
/// resolved through <c>ITenantSessionOverride</c> inside
/// <see cref="MobileConfigPublishedReadService"/> exactly like
/// <c>ConsumerContentController</c>'s <c>{tenantId}</c> route segment already does — a
/// consumer/anonymous session structurally never carries an <c>app.tenant_id</c> claim (see
/// <c>TenantConnectionInterceptor</c>/<c>TenantSessionOverride</c> remarks), so there is no
/// implicit "current tenant" this endpoint could otherwise fall back to. A query parameter (not a
/// route segment, unlike <c>ConsumerContentController</c>) was chosen specifically so the path
/// itself stays byte-for-byte spec-compliant while still explicitly, safely identifying which
/// tenant's config to serve.
///
/// AUTH DECISION: <c>[AllowAnonymous]</c> — same posture as <c>ConsumerContentController</c>.
/// MASTER SPEC §12's "discover before joining" flow (a shopper can browse a retailer's app before
/// creating an account/joining) requires this to work with no consumer JWT at all, and an
/// authenticated consumer session gains nothing extra here (the document is identical for every
/// viewer of a given tenant), so there is no reason to diverge from the existing precedent.
/// </summary>
[ApiController]
[Route("api/v1/mobile")]
[AllowAnonymous]
public sealed class MobileConfigController : ControllerBase
{
    private readonly IMobileConfigPublishedReadService _service;

    public MobileConfigController(IMobileConfigPublishedReadService service) => _service = service;

    /// <summary>
    /// Returns the tenant's current published mobile configuration document, or <c>304</c> when
    /// the caller's <c>If-None-Match</c> already matches the current ETag. Draft/archived versions
    /// are never reachable here under any parameter — see
    /// <see cref="MobileConfigPublishedReadService"/>.
    /// </summary>
    [HttpGet("config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConfig([FromQuery] Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "tenantId query parameter is required." });

        var (documentJson, etag, error) = await _service.GetPublishedConfigAsync(tenantId, ct);
        if (documentJson is null || etag is null)
            return NotFound(new { error = error!.Message });

        Response.Headers.ETag = etag;

        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch) && MatchesCurrentETag(ifNoneMatch, etag))
            return StatusCode(StatusCodes.Status304NotModified);

        return Content(documentJson, "application/json");
    }

    /// <summary>
    /// <c>If-None-Match</c> may carry <c>*</c> or a comma-separated list of ETags. This endpoint's
    /// own ETags are always a quoted 64-char SHA-256 hex string, so a plain per-token equality
    /// check (after trimming) is sufficient — no weak-ETag ("W/") handling is needed because this
    /// endpoint never emits one.
    /// </summary>
    private static bool MatchesCurrentETag(string ifNoneMatchHeader, string currentETag) =>
        ifNoneMatchHeader == "*" ||
        ifNoneMatchHeader.Split(',').Select(v => v.Trim()).Any(v => v == currentETag);
}
