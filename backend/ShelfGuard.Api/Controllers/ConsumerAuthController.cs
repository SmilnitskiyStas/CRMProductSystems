using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
[AllowAnonymous]
public sealed class ConsumerAuthController : ControllerBase
{
    private readonly IConsumerAuthService _auth;

    public ConsumerAuthController(IConsumerAuthService auth) => _auth = auth;

    [HttpPost("register")]
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
}
