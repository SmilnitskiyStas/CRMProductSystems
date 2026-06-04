using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Auth.Dtos;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private const string RefreshTokenCookie = "refreshToken";

    public AuthController(IAuthService auth) => _auth = auth;

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var (response, error) = await _auth.LoginAsync(request.Email, request.Password, ct);
        if (error is not null)
            return Unauthorized(new { error });

        SetRefreshTokenCookie(response!.RefreshToken);

        return Ok(new { response.AccessToken, response.User });
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var rawToken = Request.Cookies[RefreshTokenCookie];
        if (string.IsNullOrEmpty(rawToken))
            return Unauthorized(new { error = "Refresh token not found." });

        var (response, error) = await _auth.RefreshAsync(rawToken, ct);
        if (error is not null)
        {
            DeleteRefreshTokenCookie();
            return Unauthorized(new { error });
        }

        SetRefreshTokenCookie(response!.RefreshToken);

        return Ok(new { response.AccessToken, response.User });
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var rawToken = Request.Cookies[RefreshTokenCookie];
        if (!string.IsNullOrEmpty(rawToken))
            await _auth.RevokeAsync(rawToken, ct);

        DeleteRefreshTokenCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(AuthUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userId, out var id))
            return Unauthorized();

        var user = await _auth.GetCurrentUserAsync(id, ct);
        return user is null ? Unauthorized() : Ok(user);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private void SetRefreshTokenCookie(string rawToken)
    {
        Response.Cookies.Append(RefreshTokenCookie, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !HttpContext.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase),
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
        });
    }

    private void DeleteRefreshTokenCookie() =>
        Response.Cookies.Delete(RefreshTokenCookie);
}
