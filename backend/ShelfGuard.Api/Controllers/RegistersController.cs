using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Cash registers for the cashier app: manager CRUD + activation codes, the anonymous device
/// endpoints (activate, PIN session), operator sign-out and employee PIN management.
/// Thin: all logic lives in <see cref="IRegisterService"/>.
/// </summary>
[ApiController]
[Route("api/pos/registers")]
public sealed class RegistersController : ControllerBase
{
    public const string DeviceTokenHeader = "X-Register-Token";

    private readonly IRegisterService _registers;

    public RegistersController(IRegisterService registers) => _registers = registers;

    // ── Manager endpoints ──────────────────────────────────────────────────

    [HttpGet]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [RequireModule("pos")]
    [ProducesResponseType(typeof(IReadOnlyList<RegisterDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid? storeId, CancellationToken ct) =>
        TryGetCaller(out var caller) ? Ok(await _registers.ListAsync(caller, storeId, ct)) : Forbid();

    /// <summary>Creates a register and returns its one-time activation code (shown once).</summary>
    [HttpPost]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [RequireModule("pos")]
    [ProducesResponseType(typeof(RegisterCreatedDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateRegisterRequest request, CancellationToken ct)
    {
        if (!TryGetCaller(out var caller)) return Forbid();
        var (result, error, status) = await _registers.CreateAsync(caller, request, ct);
        return error is null ? StatusCode(StatusCodes.Status201Created, result) : Fail(error, status);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [RequireModule("pos")]
    [ProducesResponseType(typeof(RegisterDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRegisterRequest request, CancellationToken ct)
    {
        if (!TryGetCaller(out var caller)) return Forbid();
        var (result, error, status) = await _registers.UpdateAsync(caller, id, request, ct);
        return error is null ? Ok(result) : Fail(error, status);
    }

    /// <summary>Issues a fresh single-use activation code (24 h).</summary>
    [HttpPost("{id:guid}/activation-code")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [RequireModule("pos")]
    [ProducesResponseType(typeof(ActivationCodeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> IssueActivationCode(Guid id, CancellationToken ct)
    {
        if (!TryGetCaller(out var caller)) return Forbid();
        var (result, error, status) = await _registers.IssueActivationCodeAsync(caller, id, ct);
        return error is null ? Ok(result) : Fail(error, status);
    }

    /// <summary>Revokes the register's device token and ends its operator sessions.</summary>
    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [RequireModule("pos")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        if (!TryGetCaller(out var caller)) return Forbid();
        var (_, error, status) = await _registers.RevokeAsync(caller, id, ct);
        return error is null ? NoContent() : Fail(error, status);
    }

    /// <summary>Re-enables a register locked by the daily PIN-failure cap.</summary>
    [HttpPost("{id:guid}/unlock")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [RequireModule("pos")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct)
    {
        if (!TryGetCaller(out var caller)) return Forbid();
        var (_, error, status) = await _registers.UnlockAsync(caller, id, ct);
        return error is null ? NoContent() : Fail(error, status);
    }

    /// <summary>
    /// (Re)generates an employee's POS PIN. The server picks it (unique within the store; collisions
    /// are retried silently so taken PINs are never revealed) and returns it once in the response.
    /// </summary>
    [HttpPost("pins/{userId:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [RequireModule("pos")]
    [EnableRateLimiting("register-pin-manage")]
    [ProducesResponseType(typeof(SetPinResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetPin(Guid userId, CancellationToken ct)
    {
        if (!TryGetCaller(out var caller)) return Forbid();
        var (result, error, status) = await _registers.SetPinAsync(caller, userId, ct);
        return error is null ? Ok(result) : Fail(error, status);
    }

    [HttpDelete("pins/{userId:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [RequireModule("pos")]
    [EnableRateLimiting("register-pin-manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearPin(Guid userId, CancellationToken ct)
    {
        if (!TryGetCaller(out var caller)) return Forbid();
        var (_, error, status) = await _registers.ClearPinAsync(caller, userId, ct);
        return error is null ? NoContent() : Fail(error, status);
    }

    // ── Device endpoints (anonymous: no user JWT exists yet) ───────────────

    /// <summary>Exchanges a single-use activation code for the register's device token.</summary>
    [HttpPost("activate")]
    [AllowAnonymous]
    [EnableRateLimiting("register-activate")]
    [ProducesResponseType(typeof(ActivateRegisterResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate([FromBody] ActivateRegisterRequest request, CancellationToken ct)
    {
        var (result, error, status) = await _registers.ActivateAsync(request, ct);
        return error is null ? Ok(result) : Fail(error, status);
    }

    /// <summary>Device token (X-Register-Token header) + PIN -> short-lived operator JWT.</summary>
    [HttpPost("session")]
    [AllowAnonymous]
    [EnableRateLimiting("register-session")]
    [ProducesResponseType(typeof(RegisterSessionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> StartSession([FromBody] StartSessionRequest request, CancellationToken ct)
    {
        var deviceToken = Request.Headers[DeviceTokenHeader].FirstOrDefault();
        var (result, error, status) = await _registers.StartSessionAsync(deviceToken, request, ct);
        return error is null ? Ok(result) : Fail(error, status);
    }

    /// <summary>Operator sign-out (manual or idle). Requires the operator JWT.</summary>
    [HttpPost("session/end")]
    [Authorize(Policy = AppPolicies.CanAccessPos)]
    [AllowOperatorToken]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> EndSession([FromBody] EndSessionRequest? request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("session_id"), out var sessionId)
            || !Guid.TryParse(User.FindFirstValue("register_id"), out var registerId))
            return Forbid(); // not an operator token

        var (_, error, status) = await _registers.EndSessionAsync(sessionId, registerId, request?.Reason, ct);
        return error is null ? NoContent() : Fail(error, status);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private IActionResult Fail(string? error, int? status) =>
        StatusCode(status ?? StatusCodes.Status400BadRequest, new { error });

    private bool TryGetCaller(out RegisterCaller caller)
    {
        caller = null!;
        if (!Guid.TryParse(User.FindFirstValue("tenant_id"), out var tenantId) || tenantId == Guid.Empty) return false;
        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdRaw, out var userId)) return false;
        Guid? storeId = Guid.TryParse(User.FindFirstValue("store_id"), out var sid) && sid != Guid.Empty ? sid : null;
        caller = new RegisterCaller(tenantId, userId, User.FindFirstValue(ClaimTypes.Role) ?? string.Empty, storeId);
        return true;
    }
}
