using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Auth.Dtos;
using ShelfGuard.Application.Features.Users;
using ShelfGuard.Application.Features.Users.Dtos;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserService _users;
    private const string RefreshTokenCookie = "refreshToken";

    public AuthController(IAuthService auth, IUserService users)
    {
        _auth  = auth;
        _users = users;
    }

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
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        // Impersonation tokens embed role/tenant_id in claims but keep sub = providerId.
        // Fetching by sub would return the provider's DB row (role: "provider") — wrong.
        // Read the already-verified JWT claims directly instead.
        if (User.FindFirstValue("impersonated") == "true")
        {
            var role     = User.FindFirstValue(ClaimTypes.Role) ?? "enterprise_admin";
            var email    = User.FindFirstValue(ClaimTypes.Email)
                        ?? User.FindFirstValue(JwtRegisteredClaimNames.Email)
                        ?? string.Empty;
            var tenantIdRaw = User.FindFirstValue("tenant_id");
            Guid? tenantId  = Guid.TryParse(tenantIdRaw, out var tid) ? tid : null;

            return Ok(new AuthUserDto(userId.Value, email, email, role, tenantId, StoreId: null));
        }

        var user = await _auth.GetCurrentUserAsync(userId.Value, ct);
        return user is null ? Unauthorized() : Ok(user);
    }

    /// <summary>Updates the currently authenticated user's own profile.</summary>
    [HttpPut("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateMe(
        [FromBody] UpdateMyProfileRequest request, CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var (user, error) = await _users.UpdateMyProfileAsync(userId.Value, request, ct);
        return user is null ? BadRequest(new { error }) : Ok(user);
    }

    /// <summary>Changes the password for the currently authenticated user.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var error = await _users.ChangePasswordAsync(userId.Value, request, ct);
        return error is null ? NoContent() : BadRequest(new { error });
    }

    /// <summary>Links a Telegram chat to the current user.</summary>
    [HttpPost("telegram/link")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LinkTelegram(
        [FromBody] LinkTelegramRequest request, CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var error = await _users.LinkTelegramAsync(userId.Value, request.ChatId, ct);
        return error is null ? NoContent() : BadRequest(new { error });
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? ResolveUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

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
