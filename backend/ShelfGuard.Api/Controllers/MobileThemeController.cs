using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Tenant-admin read/write of the acting tenant's <c>MobileTheme</c> row (TASK-536, Retailer Admin
/// Design section — fills the <c>/consumer-app/design</c> placeholder TASK-535 scaffolded). Same
/// tenant-admin-editable-field shape as <see cref="LoyaltySettingsController"/>
/// (<c>AtLeastEnterpriseAdmin</c>, <c>ITenantContext</c>, GET proposes defaults / PUT upserts),
/// applied to <c>MobileTheme</c>'s multiple whitelisted fields instead of one.
///
/// Versioned under <c>/api/v1/</c> per decision 2 (TASK-556): every new consumer-platform endpoint
/// from Stage B onward is versioned, including this Stage C admin surface — not just the
/// consumer-facing <c>GET /api/v1/mobile/config</c> (TASK-534). This is a DIFFERENT security
/// posture than that endpoint ([AllowAnonymous], anonymous consumer reads) — deliberately a
/// separate controller rather than adding authenticated routes onto
/// <see cref="MobileConfigController"/>.
///
/// LIVE-EFFECT CAVEAT: see <see cref="MobileThemeService"/>'s remarks. A successful <c>PUT</c> here
/// takes effect in <c>GET /api/v1/mobile/config</c> immediately — there is no draft/publish
/// protection for theme yet (pending TASK-544).
/// </summary>
[ApiController]
[Route("api/v1/mobile/theme")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
public sealed class MobileThemeController : ControllerBase
{
    private readonly IMobileThemeService _theme;
    private readonly ITenantContext _tenantContext;

    public MobileThemeController(IMobileThemeService theme, ITenantContext tenantContext)
    {
        _theme = theme;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(MobileThemeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var dto = await _theme.GetThemeAsync(tenantId.Value, ct);
        return Ok(dto);
    }

    [HttpPut]
    [ProducesResponseType(typeof(MobileThemeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update([FromBody] UpdateMobileThemeRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (dto, errors) = await _theme.UpdateThemeAsync(tenantId.Value, GetUserId(), request, ct);
        if (errors.Count > 0)
            return BadRequest(new { errors });

        return Ok(dto);
    }

    // ── helpers ────────────────────────────────────────────────────────

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
