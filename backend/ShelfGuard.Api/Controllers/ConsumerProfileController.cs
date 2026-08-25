using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.ConsumerProfile;
using ShelfGuard.Application.Features.ConsumerProfile.Dtos;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Consumer-facing self-service profile editing (TASK-614) — requires a ConsumerAccount session
/// JWT (claim "consumer_account_id"), never a staff token. Same authorization shape as
/// <see cref="ConsumerLoyaltyController"/>: the claim is the whole boundary (belt-and-suspenders,
/// since ConsumerAccount/ConsumerAccountProfileChange carry no RLS at all — see
/// IConsumerAccountRepository's doc).
/// </summary>
[ApiController]
[Route("api/consumer/profile")]
[Authorize]
public sealed class ConsumerProfileController : ControllerBase
{
    private readonly IConsumerProfileService _profile;

    public ConsumerProfileController(IConsumerProfileService profile) => _profile = profile;

    [HttpGet]
    [ProducesResponseType(typeof(ConsumerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (profile, error, statusCode) = await _profile.GetProfileAsync(consumerId.Value, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(profile);
    }

    [HttpPut]
    [ProducesResponseType(typeof(ConsumerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromBody] UpdateConsumerProfileRequest request, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (profile, error, statusCode) = await _profile.UpdateNameOrEmailAsync(
            consumerId.Value, request.FullName, request.Email, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(profile);
    }

    /// <summary>Separate route from <see cref="Update"/> — phone change has a different verification gate (password re-entry).</summary>
    [HttpPut("phone")]
    [ProducesResponseType(typeof(ConsumerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangePhone([FromBody] ChangeConsumerPhoneRequest request, CancellationToken ct)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (profile, error, statusCode) = await _profile.ChangePhoneAsync(
            consumerId.Value, request.NewPhone, request.CurrentPassword, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(profile);
    }

    [HttpGet("history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var consumerId = ResolveConsumerAccountId();
        if (consumerId is null) return Forbid();

        var (history, error, statusCode) = await _profile.GetProfileChangeHistoryAsync(
            consumerId.Value, page, pageSize, ct);
        if (error is not null)
            return StatusCode(statusCode ?? 400, new { error });

        return Ok(history);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Requires the dedicated "consumer_account_id" claim rather than falling back to
    /// sub/NameIdentifier — a staff JWT never carries this claim, so this alone is enough to
    /// reject a staff session hitting a consumer-only endpoint. Mirrors
    /// ConsumerLoyaltyController.ResolveConsumerAccountId exactly.
    /// </summary>
    private Guid? ResolveConsumerAccountId()
    {
        var claim = User.FindFirst("consumer_account_id")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
