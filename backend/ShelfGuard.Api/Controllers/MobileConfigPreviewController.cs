using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Staff-only, read-only preview of the acting tenant's current DRAFT mobile configuration
/// (TASK-547 — closes out Stage D). Lets a Retailer Admin see exactly what publishing right now
/// would produce, in the same document shape <c>GET /api/v1/mobile/config</c> (TASK-534) serves
/// for a published config — without publishing anything.
///
/// SEPARATE CONTROLLER, NOT FOLDED INTO <see cref="MobileConfigDraftController"/> (TASK-538b) OR
/// <see cref="MobileConfigController"/> (TASK-534): same "one concern per controller" shape
/// <see cref="MobileConfigPublishController"/> (TASK-544) already established for this feature —
/// draft CRUD, publish, and (now) preview are different concerns. Critically, this must NOT live
/// on <see cref="MobileConfigController"/>: that controller carries a controller-level
/// <c>[AllowAnonymous]</c> for its own consumer-facing route, and ASP.NET Core's authorization
/// middleware skips the authorize check for an endpoint entirely whenever ANY
/// <c>AllowAnonymousAttribute</c> is present in that endpoint's metadata — action-level
/// <c>[Authorize]</c> would NOT override a controller-level <c>[AllowAnonymous]</c>. A dedicated
/// controller with its own <c>[Authorize(Policy = AtLeastEnterpriseAdmin)]</c> avoids that trap
/// entirely rather than relying on attribute-ordering behavior. Route
/// <c>api/v1/mobile/config/preview</c> is a sibling of, not nested under,
/// <c>api/v1/mobile/config/draft</c>/<c>api/v1/mobile/config/publish</c> — same flat sibling
/// shape those two already use, so no absolute-route override is needed on another controller.
///
/// Same <c>AtLeastEnterpriseAdmin</c> / <see cref="ITenantContext"/>-resolved-tenant shape
/// <see cref="MobileConfigDraftController"/>/<see cref="MobileConfigPublishController"/> already
/// established — this is a staff/admin action on their OWN tenant, not a cross-tenant consumer
/// read, so it deliberately does NOT use <c>ITenantSessionOverride</c> (that belongs only to the
/// anonymous <see cref="MobileConfigController"/>).
///
/// NEVER MUTATES: this controller only ever calls
/// <see cref="IMobileConfigPreviewService.GetPreviewAsync"/> — no draft save, no publish, no
/// version-status change. See that service for the read-only guarantee.
/// </summary>
[ApiController]
[Route("api/v1/mobile/config/preview")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
[RequireModule("mobile_app")] // TASK-674: App Builder is part of the "Застосунок" section
public sealed class MobileConfigPreviewController : ControllerBase
{
    private readonly IMobileConfigPreviewService _preview;
    private readonly ITenantContext _tenantContext;

    public MobileConfigPreviewController(IMobileConfigPreviewService preview, ITenantContext tenantContext)
    {
        _preview = preview;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Returns the tenant's current draft, composed into the same document shape
    /// <c>GET /api/v1/mobile/config</c> returns for a published config (with <c>theme</c> composed
    /// live from the tenant's current <c>MobileTheme</c> row), plus a leading <c>hasDraft</c> flag.
    /// Always <c>200</c> for an authorized staff request — a tenant with no draft yet gets an
    /// empty/default document (<c>hasDraft: false</c>), never a bare <c>404</c>, same convention
    /// <see cref="MobileConfigDraftController.Get"/> already established.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var documentJson = await _preview.GetPreviewAsync(tenantId.Value, ct);
        return Content(documentJson, "application/json");
    }
}
