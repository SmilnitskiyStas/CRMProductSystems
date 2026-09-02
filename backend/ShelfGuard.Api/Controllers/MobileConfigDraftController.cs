using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Tenant-admin read/write of the acting tenant's draft <c>MobileConfigurationVersion</c>
/// (TASK-538b) — the HTTP layer TASK-532 deliberately deferred ("no admin UI consumer exists yet")
/// wrapping <see cref="IMobileConfigDraftService.SaveDraftAsync"/>/
/// <see cref="IMobileConfigDraftService.GetDraftAsync"/> verbatim; no draft orchestration logic
/// lives here — that stays in <see cref="MobileConfigDraftService"/> untouched. TASK-539's App
/// Builder canvas is the first real consumer.
///
/// Same <c>AtLeastEnterpriseAdmin</c> / <see cref="ITenantContext"/> / separate-controller-from-
/// the-anonymous-<see cref="MobileConfigController"/> shape <see cref="MobileThemeController"/>
/// (TASK-536) already established for this exact "staff/admin write vs. anonymous consumer read"
/// split — see that controller's remarks. This is a staff/admin write, not a consumer/cross-tenant
/// read, so it deliberately does NOT use <c>ITenantSessionOverride</c> (that belongs to the
/// anonymous <see cref="MobileConfigController"/> TASK-534 built). Versioned under <c>/api/v1/</c>
/// per decision 2 (TASK-556).
/// </summary>
[ApiController]
[Route("api/v1/mobile/config/draft")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
[RequireModule("mobile_app")] // TASK-674: App Builder is part of the "Застосунок" section
public sealed class MobileConfigDraftController : ControllerBase
{
    private readonly IMobileConfigDraftService _drafts;
    private readonly ITenantContext _tenantContext;

    public MobileConfigDraftController(IMobileConfigDraftService drafts, ITenantContext tenantContext)
    {
        _drafts = drafts;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Returns the tenant's current draft, or <see cref="MobileConfigDraftResponse.Empty"/> when
    /// the tenant has never saved one yet (a brand-new tenant's first App Builder visit) — always
    /// <c>200</c>, never <c>404</c>, matching the "propose defaults on GET" convention
    /// <see cref="MobileThemeService"/>/<c>LoyaltyService.GetSettingsAsync</c> already use elsewhere.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(MobileConfigDraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var draft = await _drafts.GetDraftAsync(tenantId.Value, ct);
        return Ok(draft is null ? MobileConfigDraftResponse.Empty() : MobileConfigDraftResponse.FromDraft(draft));
    }

    /// <summary>
    /// Validates and saves <paramref name="request"/> as the tenant's draft via
    /// <see cref="IMobileConfigDraftService.SaveDraftAsync"/>. Validation failures return the same
    /// <c>{ errors: [{ field, message }] }</c> shape <see cref="MobileThemeController"/> already
    /// established, so the frontend's existing <c>ApiError.body</c> handling
    /// (<c>frontend/lib/api.ts</c>) covers this endpoint for free.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(MobileConfigDraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Save([FromBody] SaveMobileConfigDraftRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (draft, errors) =
            await _drafts.SaveDraftAsync(tenantId.Value, GetUserId(), request.ConfigurationJson, ct);
        if (errors.Count > 0)
            return BadRequest(new { errors });

        return Ok(MobileConfigDraftResponse.FromDraft(draft!));
    }

    // ── helpers ────────────────────────────────────────────────────────

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
