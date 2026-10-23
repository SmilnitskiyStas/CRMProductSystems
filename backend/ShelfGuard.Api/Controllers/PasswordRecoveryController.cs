using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Auth.Dtos;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class PasswordRecoveryController(IPasswordRecoveryService recovery) : ControllerBase
{
    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await recovery.RequestAsync(request.Email, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Confirm([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var error = await recovery.ConfirmAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        if (error is not null) return BadRequest(new { error });
        Response.Headers.CacheControl = "no-store";
        Response.Cookies.Delete("refreshToken");
        return NoContent();
    }
}
