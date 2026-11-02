using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.ConsumerAuth;
using ShelfGuard.Application.Features.ConsumerAuth.Dtos;
using ShelfGuard.Application.Features.MobileAuth.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Single mobile entry point over the existing staff and consumer identity systems.
/// Consumer credentials authorize personal access only. Workspace access requires the
/// staff password and, when enabled, the staff second factor.
/// </summary>
[ApiController]
[Route("api/mobile-auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[AllowAnonymous]
public sealed class MobileAuthController : ControllerBase
{
    private readonly IAuthService _staffAuth;
    private readonly IConsumerAuthService _consumerAuth;
    private readonly IUserRepository _users;
    private readonly IConsumerAccountRepository _consumers;

    public MobileAuthController(
        IAuthService staffAuth,
        IConsumerAuthService consumerAuth,
        IUserRepository users,
        IConsumerAccountRepository consumers)
    {
        _staffAuth = staffAuth;
        _consumerAuth = consumerAuth;
        _users = users;
        _consumers = consumers;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(MobileLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] ConsumerRegisterRequest request, CancellationToken ct)
    {
        var (consumer, error) = await _consumerAuth.RegisterAsync(request, ct);
        if (error is not null)
            return error.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                ? Conflict(new { error })
                : BadRequest(new { error });

        // SEC-001: self-declared contacts are not proof of a staff identity.
        // Registration must never mint a workspace session or a staff MFA challenge.
        return Ok(MobileLoginResponseFactory.ForConsumer(
            consumer!.AccessToken,
            consumer.ConsumerAccountId,
            consumer.FullName,
            consumer.Phone, consumer.RefreshToken));
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(MobileLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] MobileLoginRequest request, CancellationToken ct)
    {
        var identifier = request.Identifier.Trim();

        var consumerAccount = await FindConsumerAsync(identifier, ct);
        if (consumerAccount is not null)
        {
            var (consumer, consumerError) = await _consumerAuth.LoginAsync(
                new ConsumerLoginRequest(consumerAccount.Phone, request.Password), ct);
            if (consumerError is not null)
                return Unauthorized(new { error = "Invalid identifier or password." });

            // SEC-001: neither a matching phone nor a mutable email grants workspace
            // access. Do not attempt staff authentication using consumer credentials.
            return Ok(MobileLoginResponseFactory.ForConsumer(
                consumer!.AccessToken,
                consumer.ConsumerAccountId,
                consumer.FullName,
                consumer.Phone, consumer.RefreshToken));
        }

        // Backward-compatible path for invited staff who have not created their personal
        // mobile account yet. Both email and a normalized phone resolve the same User row.
        var staffUser = await FindStaffAsync(identifier, ct);
        if (staffUser is null)
            return Unauthorized(new { error = "Invalid identifier or password." });

        var outcome = await _staffAuth.LoginAsync(staffUser.Email, request.Password, ClientIp(), ct);
        if (outcome.Error is not null)
            return Unauthorized(new { error = "Invalid identifier or password." });

        if (outcome.ChallengeToken is not null)
            return Ok(new { requiresTwoFactor = true, challengeToken = outcome.ChallengeToken });
        if (outcome.EnrollmentToken != null) return Ok(new { requiresMfaEnrollment = true, enrollmentToken = outcome.EnrollmentToken });

        Response.Cookies.Append("refreshToken", outcome.Response!.RefreshToken, new CookieOptions
        { HttpOnly = true, Secure = !Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase), SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7) });
        return Ok(MobileLoginResponseFactory.ForStaff(
            outcome.Response!.AccessToken, outcome.Response.User));
    }

    private async Task<ConsumerAccount?> FindConsumerAsync(string identifier, CancellationToken ct)
    {
        if (identifier.Contains('@', StringComparison.Ordinal))
            return await _consumers.GetByEmailAsync(identifier.Trim().ToLowerInvariant(), ct);

        var phone = PhoneNormalizer.Normalize(identifier);
        return phone is null ? null : await _consumers.GetByPhoneAsync(phone, ct);
    }

    private async Task<User?> FindStaffAsync(string identifier, CancellationToken ct)
    {
        if (identifier.Contains('@', StringComparison.Ordinal))
            return await _users.GetByEmailAsync(identifier.Trim().ToLowerInvariant(), ct);

        var phone = PhoneNormalizer.Normalize(identifier);
        return phone is null ? null : await _users.GetByPhoneAsync(phone, ct);
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
