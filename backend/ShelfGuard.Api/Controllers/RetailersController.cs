using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// TASK-548 (Stage E): consumer-facing retailer discovery — generalizes
/// <see cref="ConsumerLoyaltyController"/>'s tenant-id-addressed network catalogue/join endpoints
/// into a slug-addressed <c>/api/v1/</c> surface (decision 2's versioning scope: only new
/// consumer-platform endpoints get the <c>/api/v1/</c> prefix going forward). Purely additive —
/// <see cref="ConsumerLoyaltyController"/>'s <c>GET /api/consumer/loyalty/networks</c> and
/// <c>POST /api/consumer/loyalty/{tenantId}/join</c> are kept exactly as they are today, as a
/// permanent (not time-boxed) alias; see task log 548 for why no deprecation timeline was
/// invented for them.
///
/// Class-level auth posture is <see cref="ConsumerLoyaltyController"/>'s: requires a
/// ConsumerAccount session JWT (claim "consumer_account_id"), never a staff token. Deliberately
/// NOT gated by [RequireModule("loyalty")] for the same reason — that filter reads the
/// "tenant_id" claim, which a consumer session never carries (cross-tenant by design). Module
/// activation is enforced inside <see cref="ILoyaltyService"/> itself (decision 1's accepted
/// consequence: a tenant without "loyalty" enabled is unjoinable/undiscoverable, by design — no
/// new membership schema).
///
/// EXCEPTION (TASK-549): <see cref="GetRetailerPublic"/> below carries its own action-level
/// <c>[AllowAnonymous]</c>, which correctly overrides this controller's class-level
/// <c>[Authorize]</c> for that one action only (the standard ASP.NET Core pattern — see
/// AuthController's login/refresh/2fa-verify actions for the same technique used elsewhere in
/// this codebase). It exists for the QR/deep-link onboarding web fallback page, reached before
/// the scanner has any consumer session at all — see that action's doc and
/// <see cref="ILoyaltyService.GetPublicRetailerInfoAsync"/> for why it is a distinct
/// route/DTO/service method rather than a relaxation of <see cref="GetRetailer"/>.
/// </summary>
[ApiController]
[Route("api/v1/retailers")]
[Authorize]
public sealed class RetailersController : ControllerBase
{
    private readonly ILoyaltyService _loyalty;

    public RetailersController(ILoyaltyService loyalty) => _loyalty = loyalty;

    /// <summary>Lists retailers available to the calling consumer — same eligibility rule as
    /// <see cref="ConsumerLoyaltyController.GetNetworks"/>, plus each entry's <c>Slug</c>.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LoyaltyNetworkSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRetailers(CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();
        return Ok(await _loyalty.GetNetworksForConsumerAsync(consumerId.Value, ct));
    }

    /// <summary>Single retailer lookup by slug. 404 for an unknown, inactive, or
    /// loyalty-module-less/disabled slug (indistinguishable from "unknown" to the caller).</summary>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(LoyaltyNetworkSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRetailer(string slug, CancellationToken ct)
    {
        if (ResolveConsumerAccountId() is null) return Forbid();

        var (network, error, statusCode) = await _loyalty.GetNetworkBySlugAsync(slug, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(network);
    }

    /// <summary>Joins (or idempotently returns the existing/reactivated membership for) the
    /// retailer's loyalty program. Same underlying logic as
    /// <see cref="ConsumerLoyaltyController.Join"/>, resolved from a slug instead of a tenant id.</summary>
    [HttpPost("{slug}/join")]
    [ProducesResponseType(typeof(LoyaltyMembershipSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Join(string slug, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (membership, error, statusCode) = await _loyalty.JoinBySlugAsync(consumerId.Value, slug, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(membership);
    }

    /// <summary>
    /// Leaves the retailer's loyalty program (TASK-548's new capability — see
    /// <see cref="ILoyaltyService.LeaveAsync"/> for the soft-deactivation design). Idempotent:
    /// leaving twice both return 204. 404 when the consumer has no membership at this retailer
    /// (including an unknown slug).
    /// </summary>
    [HttpDelete("{slug}/membership")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LeaveMembership(string slug, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (success, error, statusCode) = await _loyalty.LeaveBySlugAsync(consumerId.Value, slug, ct);
        if (!success)
            return StatusCode(statusCode ?? 400, new { error });

        return NoContent();
    }

    /// <summary>
    /// TASK-549: anonymous public lookup for the QR/deep-link onboarding web fallback page
    /// (<c>https://app.domain/join/{slug}</c>) — reached by anyone who scans a retailer's QR
    /// code before installing the app or logging in, so it cannot require a ConsumerAccount
    /// session like every other action in this controller. Distinct route AND distinct minimal
    /// <see cref="RetailerPublicInfoDto"/> from <see cref="GetRetailer"/> above (no store list,
    /// no internal tenant guid) — see <see cref="ILoyaltyService.GetPublicRetailerInfoAsync"/>
    /// for the full rationale. 404 for an unknown, inactive, loyalty-module-less, or
    /// program-paused slug — indistinguishable from each other by design (same doc).
    ///
    /// TASK-554: rate-limited (unlike every other action here) — it is the one action on this
    /// controller with no ConsumerAccount JWT/per-account accountability, making it the most
    /// attractive target on the surface for slug enumeration. See the "retailer-public-lookup"
    /// policy in <c>Program.cs</c> for the limit and reasoning.
    /// </summary>
    [HttpGet("{slug}/public")]
    [AllowAnonymous]
    [EnableRateLimiting("retailer-public-lookup")]
    [ProducesResponseType(typeof(RetailerPublicInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRetailerPublic(string slug, CancellationToken ct)
    {
        var (info, error, statusCode) = await _loyalty.GetPublicRetailerInfoAsync(slug, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(info);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>Same contract as <see cref="ConsumerLoyaltyController"/>'s identical helper —
    /// see its remarks. Not shared via a base class, matching this repo's existing convention of
    /// each consumer/staff controller carrying its own small claim-resolution helper (see
    /// <c>backend-structure.md</c>'s note on <c>ResolveTenantId()</c> not yet centralized).</summary>
    private Guid? ResolveConsumerAccountId()
    {
        var claim = User.FindFirst("consumer_account_id")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
