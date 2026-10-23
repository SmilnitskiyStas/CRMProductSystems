using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using ShelfGuard.Application.Features.ConsumerAuth;
using ShelfGuard.Application.Features.ConsumerAuth.Dtos;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Registration/login for the loyalty program's consumer wallet identity (Loyalty Фаза 0,
/// TASK-405) — a wholly separate auth flow from staff /api/auth (ConsumerAccount, never a
/// User row). Reuses the existing "auth-login" rate-limit policy (TASK-329) rather than
/// introducing a parallel one.
/// </summary>
[ApiController]
[Route("api/consumer-auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ConsumerAuthController : ControllerBase
{
    private readonly IConsumerAuthService _auth;

    public ConsumerAuthController(IConsumerAuthService auth) => _auth = auth;

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(ConsumerAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] ConsumerRegisterRequest request, CancellationToken ct)
    {
        var (response, error) = await _auth.RegisterAsync(request, ct);
        if (error is not null)
            return error.Contains("already exists")
                ? Conflict(new { error })
                : BadRequest(new { error });

        return Ok(response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(ConsumerAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] ConsumerLoginRequest request, CancellationToken ct)
    {
        var (response, error) = await _auth.LoginAsync(request, ct);
        if (error is not null)
            return Unauthorized(new { error });

        return Ok(response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-refresh")]
    public async Task<IActionResult> Refresh([FromBody] ConsumerRefreshRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var (response, error) = await _auth.RefreshAsync(request.RefreshToken, ct);
        if (error == ConsumerAuthService.RotationRetry) return Conflict(new { error });
        return error is null ? Ok(response) : Unauthorized(new { error });
    }

    [HttpPost("logout")]
    [Authorize(Roles = "consumer")]
    public Task<IActionResult> Logout(CancellationToken ct) => Revoke(false, ct);

    [HttpPost("logout-all")]
    [Authorize(Roles = "consumer")]
    public Task<IActionResult> LogoutAll(CancellationToken ct) => Revoke(true, ct);

    private async Task<IActionResult> Revoke(bool all, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("consumer_account_id"), out var id)
            || !Guid.TryParse(User.FindFirstValue("sid"), out var sid)) return Unauthorized();
        await _auth.RevokeAsync(id, all ? null : sid, ct);
        return NoContent();
    }
}
