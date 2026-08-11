using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Consumer-facing loyalty wallet (Loyalty Фаза 0, TASK-405) — requires a ConsumerAccount
/// session JWT (claim "consumer_account_id"), never a staff token. Deliberately NOT gated by
/// [RequireModule("loyalty")]: that filter reads the "tenant_id" claim, which a consumer
/// session never carries (it is cross-tenant by design) — module activation is instead
/// enforced inside LoyaltyService.JoinAsync itself.
/// </summary>
[ApiController]
[Route("api/consumer/loyalty")]
[Authorize]
public sealed class ConsumerLoyaltyController : ControllerBase
{
    private readonly ILoyaltyService _loyalty;

    public ConsumerLoyaltyController(ILoyaltyService loyalty) => _loyalty = loyalty;

    [HttpPost("{tenantId:guid}/join")]
    public async Task<IActionResult> Join(Guid tenantId, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (membership, error, statusCode) = await _loyalty.JoinAsync(consumerId.Value, tenantId, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(membership);
    }

    [HttpGet("memberships")]
    public async Task<IActionResult> GetMemberships(CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var memberships = await _loyalty.GetMembershipsForConsumerAsync(consumerId.Value, ct);
        return Ok(memberships);
    }

    [HttpGet("networks")]
    [ProducesResponseType(typeof(IReadOnlyList<LoyaltyNetworkSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNetworks(CancellationToken ct)
    {
        if (ResolveConsumerAccountId() is null) return Forbid();
        return Ok(await _loyalty.GetAvailableNetworksAsync(ct));
    }

    /// <summary>
    /// TASK-499: <paramref name="tenantId"/> is optional — omit it to let the service infer the
    /// network from the consumer's memberships (0 → system default "barcode", 1 → that
    /// network's format, 2+ → 409 requiring the client to pass an explicit tenantId).
    /// </summary>
    [HttpGet("code")]
    [ProducesResponseType(typeof(LoyaltyCodeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetCode([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (code, error, statusCode) = await _loyalty.GetConsumerCodeAsync(consumerId.Value, tenantId, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(code);
    }

    /// <summary>
    /// TASK-507: which store within an already-joined network the consumer primarily shops at
    /// — a separate, additional preference from membership itself (never creates one; no
    /// membership at the given tenantId is a 403, not an implicit join).
    /// </summary>
    [HttpPut("preferred-store")]
    [ProducesResponseType(typeof(LoyaltyMembershipSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetPreferredStore([FromBody] SetPreferredStoreRequest request, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (membership, error, statusCode) = await _loyalty.SetPreferredStoreAsync(
            consumerId.Value, request.TenantId, request.StoreId, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(membership);
    }

    [HttpGet("{tenantId:guid}/history")]
    public async Task<IActionResult> GetHistory(
        Guid tenantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (history, error, statusCode) = await _loyalty.GetHistoryAsync(consumerId.Value, tenantId, page, pageSize, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(history);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Requires the dedicated "consumer_account_id" claim rather than falling back to
    /// sub/NameIdentifier — a staff JWT never carries this claim, so this alone is enough to
    /// reject a staff session hitting a consumer-only endpoint (belt-and-suspenders: RLS's
    /// consumer_self_access policy is the real boundary either way).
    /// </summary>
    private Guid? ResolveConsumerAccountId()
    {
        var claim = User.FindFirst("consumer_account_id")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
