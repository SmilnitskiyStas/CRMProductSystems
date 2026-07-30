using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OtpNet;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Auth;

/// <summary>
/// TASK-330 — 2FA TOTP flows. Uses the real Otp.NET-backed TotpService so the
/// codes verified are genuine RFC 6238 codes, and a fake deterministic HashToken
/// for recovery-code hashing.
/// </summary>
public sealed class TwoFactorAuthTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly ITotpService _totp = new TotpService(); // real implementation
    private readonly IUserPermissionGrantRepository _permissionGrants = Substitute.For<IUserPermissionGrantRepository>();
    private readonly ITenantRoleRepository _tenantRoles = Substitute.For<ITenantRoleRepository>();
    private readonly IPasswordResetTokenRepository _passwordResetTokens = Substitute.For<IPasswordResetTokenRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly AuthService _sut;

    public TwoFactorAuthTests()
    {
        _sut = new AuthService(_users, _tokens, _hasher, _jwt, _activityLogs, _totp, _permissionGrants, _tenantRoles,
            _passwordResetTokens, _notifications, NullLogger<AuthService>.Instance);
        _permissionGrants.GetActiveGrantsForUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserPermissionGrant>());
        _jwt.GenerateRefreshToken().Returns(("raw_token", "hashed_token"));
        _jwt.GenerateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<Dictionary<string, bool>?>(),
            Arg.Any<List<string>?>(), Arg.Any<List<string>?>()).Returns("access_token");
        // Deterministic fake hash — enough to prove hashes (not plaintext) are stored/compared.
        _jwt.HashToken(Arg.Any<string>()).Returns(ci => "sha256:" + ci.Arg<string>());
    }

    // ── Setup + Enable ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetupTwoFactor_stores_pending_secret_and_returns_otpauth_uri()
    {
        var user = MakeUser();
        _users.GetByIdAsync(user.Id, default).Returns(user);

        var (response, error) = await _sut.SetupTwoFactorAsync(user.Id);

        Assert.Null(error);
        Assert.NotNull(response);
        Assert.Equal(user.TotpSecret, response.Secret);
        Assert.False(user.TotpEnabled); // pending — not enabled until verified
        Assert.StartsWith("otpauth://totp/ShelfGuard:", response.OtpauthUri);
        Assert.Contains($"secret={response.Secret}", response.OtpauthUri);
        Assert.Contains("issuer=ShelfGuard", response.OtpauthUri);
    }

    [Fact]
    public async Task EnableTwoFactor_with_valid_code_activates_and_returns_8_recovery_codes()
    {
        var user = MakeUser();
        _users.GetByIdAsync(user.Id, default).Returns(user);
        await _sut.SetupTwoFactorAsync(user.Id);

        var (codes, error) = await _sut.EnableTwoFactorAsync(user.Id, CurrentCode(user));

        Assert.Null(error);
        Assert.NotNull(codes);
        Assert.Equal(8, codes.Count);
        Assert.All(codes, c => Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", c));
        Assert.True(user.TotpEnabled);
        // Stored values are hashes, never the plaintext codes
        Assert.NotNull(user.TotpRecoveryCodes);
        Assert.All(user.TotpRecoveryCodes!, h => Assert.StartsWith("sha256:", h));
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(l => l.Action == "user.2fa_enabled"), default);
    }

    [Fact]
    public async Task EnableTwoFactor_with_wrong_code_keeps_2fa_disabled()
    {
        var user = MakeUser();
        _users.GetByIdAsync(user.Id, default).Returns(user);
        await _sut.SetupTwoFactorAsync(user.Id);

        var (codes, error) = await _sut.EnableTwoFactorAsync(user.Id, "000000");

        Assert.Null(codes);
        Assert.Equal("Invalid code.", error);
        Assert.False(user.TotpEnabled);
    }

    // ── Verify (login second step) ─────────────────────────────────────────

    [Fact]
    public async Task VerifyTwoFactor_happy_path_issues_tokens()
    {
        var user = await MakeEnrolledUserAsync();
        _jwt.ValidateTwoFactorChallengeToken("challenge").Returns(user.Id);

        var (response, error) = await _sut.VerifyTwoFactorAsync("challenge", CurrentCode(user));

        Assert.Null(error);
        Assert.NotNull(response);
        Assert.Equal("access_token", response.AccessToken);
        Assert.Equal("raw_token", response.RefreshToken);
        Assert.True(response.User.TwoFactorEnabled);
        await _tokens.Received(1).AddAsync(Arg.Any<RefreshToken>(), default);
    }

    [Fact]
    public async Task VerifyTwoFactor_rejects_replayed_code()
    {
        var user = await MakeEnrolledUserAsync();
        _jwt.ValidateTwoFactorChallengeToken("challenge").Returns(user.Id);
        var code = CurrentCode(user);

        var (first, firstError) = await _sut.VerifyTwoFactorAsync("challenge", code);
        var (second, secondError) = await _sut.VerifyTwoFactorAsync("challenge", code);

        Assert.NotNull(first);
        Assert.Null(firstError);
        // Same code again — same timestep — must be rejected (anti-replay)
        Assert.Null(second);
        Assert.Equal("Invalid code.", secondError);
        Assert.Equal(1, user.FailedLoginAttempts); // replay counts toward lockout
    }

    [Fact]
    public async Task VerifyTwoFactor_rejects_invalid_challenge_token()
    {
        _jwt.ValidateTwoFactorChallengeToken("garbage").Returns((Guid?)null);

        var (response, error) = await _sut.VerifyTwoFactorAsync("garbage", "123456");

        Assert.Null(response);
        Assert.Equal("Invalid or expired challenge token.", error);
    }

    [Fact]
    public async Task VerifyTwoFactor_wrong_code_counts_toward_lockout()
    {
        var user = await MakeEnrolledUserAsync();
        _jwt.ValidateTwoFactorChallengeToken("challenge").Returns(user.Id);

        for (var i = 0; i < 5; i++)
        {
            var (_, error) = await _sut.VerifyTwoFactorAsync("challenge", "000000");
            Assert.Equal("Invalid code.", error);
        }

        Assert.True(user.IsLockedOut);
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(l => l.Action == "user.locked_out"), default);
    }

    // ── Recovery codes ─────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyTwoFactor_accepts_recovery_code_once_and_consumes_it()
    {
        var user = MakeUser();
        _users.GetByIdAsync(user.Id, default).Returns(user);
        await _sut.SetupTwoFactorAsync(user.Id);
        var (codes, _) = await _sut.EnableTwoFactorAsync(user.Id, CurrentCode(user));
        _jwt.ValidateTwoFactorChallengeToken("challenge").Returns(user.Id);

        var recoveryCode = codes![0];
        var (first, firstError) = await _sut.VerifyTwoFactorAsync("challenge", recoveryCode);

        Assert.Null(firstError);
        Assert.NotNull(first);
        Assert.Equal(7, user.TotpRecoveryCodes!.Count); // consumed

        var (second, secondError) = await _sut.VerifyTwoFactorAsync("challenge", recoveryCode);

        Assert.Null(second);
        Assert.Equal("Invalid code.", secondError); // single use
    }

    // ── Disable ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisableTwoFactor_requires_password_and_code_then_clears_state()
    {
        var user = await MakeEnrolledUserAsync();
        _hasher.Verify("correct_password", "hash").Returns(true);

        var error = await _sut.DisableTwoFactorAsync(user.Id, "correct_password", CurrentCode(user));

        Assert.Null(error);
        Assert.False(user.TotpEnabled);
        Assert.Null(user.TotpSecret);
        Assert.Null(user.TotpRecoveryCodes);
        Assert.Null(user.TotpLastTimestep);
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(l => l.Action == "user.2fa_disabled"), default);
    }

    [Fact]
    public async Task DisableTwoFactor_rejects_wrong_password()
    {
        var user = await MakeEnrolledUserAsync();
        _hasher.Verify("wrong_password", "hash").Returns(false);

        var error = await _sut.DisableTwoFactorAsync(user.Id, "wrong_password", CurrentCode(user));

        Assert.Equal("Invalid password.", error);
        Assert.True(user.TotpEnabled);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static User MakeUser() =>
        User.Create(Guid.NewGuid(), "test@example.com", "Test User", "hash", "store_manager");

    /// <summary>User with 2FA fully enabled via the real setup → enable flow.</summary>
    private async Task<User> MakeEnrolledUserAsync()
    {
        var user = MakeUser();
        _users.GetByIdAsync(user.Id, default).Returns(user);
        await _sut.SetupTwoFactorAsync(user.Id);
        var (_, error) = await _sut.EnableTwoFactorAsync(user.Id, CurrentCode(user));
        Assert.Null(error);
        // Clear the enrollment timestep so login-verification tests can reuse
        // the current 30s window without tripping anti-replay.
        typeof(User).GetProperty(nameof(User.TotpLastTimestep))!
            .SetValue(user, null, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, null, null);
        return user;
    }

    /// <summary>Genuine RFC 6238 code for the user's current secret and timestep.</summary>
    private static string CurrentCode(User user) =>
        new Totp(Base32Encoding.ToBytes(user.TotpSecret!)).ComputeTotp();
}
