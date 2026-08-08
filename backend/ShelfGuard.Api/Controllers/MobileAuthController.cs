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
/// The client never chooses a role: the identifier resolves the existing account and the
/// response explicitly describes whether the authenticated identity may enter workspace mode.
/// </summary>
[ApiController]
[Route("api/mobile-auth")]
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

        var account = await _consumers.GetByIdAsync(consumer!.ConsumerAccountId, ct);
        if (account is not null)
        {
            var linkedUser = await FindLinkedUserAsync(account, ct);
            if (linkedUser is not null && linkedUser.IsActive)
            {
                var workspaceSession = await _staffAuth.IssueLinkedMobileSessionAsync(
                    linkedUser.Id, ClientIp(), ct);
                if (workspaceSession.ChallengeToken is not null)
                    return Ok(new
                    {
                        requiresTwoFactor = true,
                        challengeToken = workspaceSession.ChallengeToken,
                        personalAccessToken = consumer.AccessToken,
                    });

                if (workspaceSession.Response is not null)
                    return Ok(MobileLoginResponseFactory.ForLinkedStaff(
                        consumer.AccessToken,
                        workspaceSession.Response.AccessToken,
                        workspaceSession.Response.User));
            }
        }

        return Ok(MobileLoginResponseFactory.ForConsumer(
            consumer.AccessToken,
            consumer.ConsumerAccountId,
            consumer.FullName,
            consumer.Phone));
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

            var linkedUser = await FindLinkedUserAsync(consumerAccount, ct);
            if (linkedUser is not null && linkedUser.IsActive)
            {
                var workspaceSession = await _staffAuth.IssueLinkedMobileSessionAsync(
                    linkedUser.Id, ClientIp(), ct);
                if (workspaceSession.ChallengeToken is not null)
                    return Ok(new
                    {
                        requiresTwoFactor = true,
                        challengeToken = workspaceSession.ChallengeToken,
                        personalAccessToken = consumer!.AccessToken,
                    });

                if (workspaceSession.Response is not null)
                    return Ok(MobileLoginResponseFactory.ForLinkedStaff(
                        consumer!.AccessToken,
                        workspaceSession.Response.AccessToken,
                        workspaceSession.Response.User));
            }

            return Ok(MobileLoginResponseFactory.ForConsumer(
                consumer!.AccessToken,
                consumer.ConsumerAccountId,
                consumer.FullName,
                consumer.Phone));
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

    private async Task<User?> FindLinkedUserAsync(ConsumerAccount consumer, CancellationToken ct)
    {
        var byPhone = await _users.GetByPhoneAsync(consumer.Phone, ct);
        if (byPhone is not null) return byPhone;

        return string.IsNullOrWhiteSpace(consumer.Email)
            ? null
            : await _users.GetByEmailAsync(consumer.Email.Trim().ToLowerInvariant(), ct);
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
