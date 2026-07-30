using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Auth.Dtos;
using ShelfGuard.Application.Features.Users;
using ShelfGuard.Application.Features.Users.Dtos;
using ShelfGuard.Domain.Interfaces;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserService _users;
    private readonly ITenantRepository _tenants;
    private const string RefreshTokenCookie = "refreshToken";

    public AuthController(IAuthService auth, IUserService users, ITenantRepository tenants)
    {
        _auth    = auth;
        _users   = users;
        _tenants = tenants;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var outcome = await _auth.LoginAsync(request.Email, request.Password, ClientIp(), ct);
        if (outcome.Error is not null)
            return Unauthorized(new { error = outcome.Error });

        // TASK-330: password ok but TOTP enabled — no tokens/cookie yet.
        if (outcome.ChallengeToken is not null)
            return Ok(new { requiresTwoFactor = true, challengeToken = outcome.ChallengeToken });

        SetRefreshTokenCookie(outcome.Response!.RefreshToken);

        return Ok(new { outcome.Response.AccessToken, outcome.Response.User });
    }

    /// <summary>Second login step: TOTP code or recovery code (TASK-330).</summary>
    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyTwoFactor(
        [FromBody] TwoFactorVerifyRequest request, CancellationToken ct)
    {
        var (response, error) = await _auth.VerifyTwoFactorAsync(
            request.ChallengeToken, request.Code, ClientIp(), ct);
        if (error is not null)
            return Unauthorized(new { error });

        SetRefreshTokenCookie(response!.RefreshToken);

        return Ok(new { response.AccessToken, response.User });
    }

    /// <summary>Generates a pending TOTP secret for enrollment (TASK-330).</summary>
    [HttpPost("2fa/setup")]
    [Authorize]
    [ProducesResponseType(typeof(TwoFactorSetupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetupTwoFactor(CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var (response, error) = await _auth.SetupTwoFactorAsync(userId.Value, ct);
        return error is not null ? BadRequest(new { error }) : Ok(response);
    }

    /// <summary>Confirms the pending secret and activates 2FA; returns one-time recovery codes.</summary>
    [HttpPost("2fa/enable")]
    [Authorize]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnableTwoFactor(
        [FromBody] TwoFactorEnableRequest request, CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var (recoveryCodes, error) = await _auth.EnableTwoFactorAsync(userId.Value, request.Code, ct);
        return error is not null ? BadRequest(new { error }) : Ok(new { recoveryCodes });
    }

    /// <summary>Disables 2FA after verifying both the password and a valid code.</summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DisableTwoFactor(
        [FromBody] TwoFactorDisableRequest request, CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var error = await _auth.DisableTwoFactorAsync(userId.Value, request.Password, request.Code, ct);
        return error is not null ? BadRequest(new { error }) : NoContent();
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-refresh")]
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

            string? tenantName = null;
            if (tenantId is not null)
            {
                var tenant = await _tenants.GetByIdAsync(tenantId.Value, ct);
                tenantName = tenant?.Name;
            }

            return Ok(new AuthUserDto(userId.Value, email, email, role, tenantId, tenantName, StoreId: null, Permissions: null, TwoFactorEnabled: false));
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

    /// <summary>
    /// First step of the forgot-password flow (TASK-456). Always responds 204 regardless of
    /// whether the email exists/is active — same no-enumeration posture as Login.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-forgot-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await _auth.ForgotPasswordAsync(request.Email, ClientIp(), ct);
        return NoContent();
    }

    /// <summary>Second step: consumes the reset token/link and sets a new password (TASK-456).</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var error = await _auth.ResetPasswordAsync(request.Token, request.NewPassword, ct);
        return error is null ? NoContent() : BadRequest(new { error });
    }

    // NOTE (2026-07-15, security fix): a `POST telegram/link` endpoint used to live here,
    // accepting a raw client-supplied Telegram chat_id and writing it straight to
    // users.TelegramChatId with zero proof of ownership — any user could paste an arbitrary
    // chat_id (their own, guessed, or leaked) and start receiving another account's
    // notifications, or silently "subscribe" to a chat that never opted in. Removed entirely;
    // account linking now goes exclusively through the verified one-time-code flow
    // (POST /api/telegram/link-code → TelegramController, consumed by the worker's
    // /start <code> listener in telegram-listener.ts), which proves the caller controls the
    // Telegram chat before TelegramChatId is ever written. See ShelfGuard.Application.Features
    // .Telegram.TelegramLinkService and .claude/logs/tasks/368_*.md for the full writeup.

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? ResolveUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>Real client IP — ForwardedHeaders middleware already resolved X-Forwarded-For.</summary>
    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

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
