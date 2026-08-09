using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Staff-facing loyalty program endpoints (Loyalty Фаза 0, TASK-405): POS code resolution,
/// manual balance adjustment, and the "staff joins their own employer's program" case
/// (plan §"Кейс 2"). Gated behind [RequireModule("loyalty")] at the controller level — a
/// staff JWT always carries tenant_id, so (unlike ConsumerLoyaltyController) the standard
/// filter applies cleanly here.
/// </summary>
[ApiController]
[Route("api/loyalty")]
[RequireModule("loyalty")]
public sealed class LoyaltyController : ControllerBase
{
    private readonly ILoyaltyService _loyalty;

    public LoyaltyController(ILoyaltyService loyalty) => _loyalty = loyalty;

    /// <summary>Scan/manual-entry resolution at the register. Any role that can run POS.</summary>
    [HttpPost("resolve-code")]
    [Authorize(Policy = AppPolicies.CanAccessPos)]
    [ProducesResponseType(typeof(ResolveLoyaltyCodeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(object), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResolveCode([FromBody] ResolveLoyaltyCodeRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var userId = GetUserId();
        if (tenantId is null || userId is null) return Forbid();

        var (result, error, statusCode) = await _loyalty.ResolveCodeAsync(tenantId.Value, userId.Value, request.Code, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(result);
    }

    /// <summary>
    /// TASK-498: staff types a phone at the register (POS "no QR" fallback) instead of the
    /// consumer manually selecting/typing a store's GUID. Resolves the ConsumerAccount for that
    /// phone and idempotently gets-or-creates its LoyaltyMembership at the caller's own tenant.
    /// Always 200 — "found: false" is the normal/expected shape for a phone that doesn't belong
    /// to any mobile-app account, or when this tenant's loyalty module isn't enabled; only a
    /// structurally-unparseable phone is a real (400) error.
    /// </summary>
    [HttpPost("resolve-or-create-by-phone")]
    [Authorize(Policy = AppPolicies.CanAccessPos)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResolveOrCreateByPhone(
        [FromBody] ResolveOrCreateMembershipByPhoneRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (result, error, statusCode) = await _loyalty.ResolveOrCreateMembershipByPhoneAsync(
            tenantId.Value, request.Phone, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        if (result is null)
            return Ok(new { found = false });

        return Ok(new
        {
            found = true,
            membershipId = result.MembershipId,
            balance = result.Balance,
            isNewMembership = result.IsNewMembership,
            consumerFullName = result.ConsumerFullName,
        });
    }

    /// <summary>Manual ledger correction — store_manager and above.</summary>
    [HttpPost("manual-adjust")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(LoyaltyMembershipSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ManualAdjust([FromBody] ManualLoyaltyAdjustRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var userId = GetUserId();
        if (tenantId is null || userId is null) return Forbid();

        var (membership, error, statusCode) = await _loyalty.ManualAdjustAsync(tenantId.Value, userId.Value, request, ct);
        if (error is not null)
            return statusCode == 404 ? NotFound(new { error }) : BadRequest(new { error });

        return Ok(membership);
    }

    /// <summary>Plan §"Кейс 2": the caller's own membership in their employer's program.</summary>
    [HttpGet("my-membership")]
    [Authorize]
    [ProducesResponseType(typeof(LoyaltyMembershipSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyMembership(CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var userId = GetUserId();
        if (tenantId is null || userId is null) return Forbid();

        var membership = await _loyalty.GetMyMembershipAsync(tenantId.Value, userId.Value, ct);
        return membership is null ? NotFound() : Ok(membership);
    }

    /// <summary>Plan §"Кейс 2": creates (or backfills) the caller's own membership by their User.Phone.</summary>
    [HttpPost("join-as-staff")]
    [Authorize]
    [ProducesResponseType(typeof(LoyaltyMembershipSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> JoinAsStaff(CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var userId = GetUserId();
        if (tenantId is null || userId is null) return Forbid();

        var (membership, error, statusCode) = await _loyalty.JoinAsStaffAsync(tenantId.Value, userId.Value, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(membership);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
