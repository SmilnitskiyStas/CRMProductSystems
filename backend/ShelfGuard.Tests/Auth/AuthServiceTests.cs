using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class AuthServiceTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly ITotpService _totp = Substitute.For<ITotpService>();
    private readonly IUserPermissionGrantRepository _permissionGrants = Substitute.For<IUserPermissionGrantRepository>();
    private readonly ITenantRoleRepository _tenantRoles = Substitute.For<ITenantRoleRepository>();
    private readonly AuthTenantScope _scope = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _sut = new AuthService(_users, _tokens, _hasher, _jwt, _activityLogs, _totp, _permissionGrants, _tenantRoles,
            NullLogger<AuthService>.Instance, Substitute.For<IMfaPolicy>(), _scope);
        _permissionGrants.GetActiveGrantsForUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserPermissionGrant>());
        _jwt.GenerateRefreshToken().Returns(("raw_token", "hashed_token"));
        _tokens.TryRotateAsync(Arg.Any<RefreshToken>(), Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>()).Returns(true);
        _jwt.HashToken("raw_token").Returns("hashed_token");
        _jwt.GenerateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<Dictionary<string, bool>?>(),
            Arg.Any<List<string>?>(), Arg.Any<List<string>?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<DateTime?>()).Returns("access_token");
    }

    // ── Login ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_returns_tokens_when_credentials_are_valid()
    {
        var user = MakeUser();
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("password123", "hash").Returns(true);

        var outcome = await _sut.LoginAsync("test@example.com", "password123");

        Assert.Null(outcome.Error);
        Assert.Null(outcome.ChallengeToken);
        Assert.NotNull(outcome.Response);
        Assert.Equal("access_token", outcome.Response.AccessToken);
        Assert.Equal("raw_token", outcome.Response.RefreshToken);
        Assert.Equal("test@example.com", outcome.Response.User.Email);
    }

    [Fact]
    public async Task LoginAsync_returns_error_when_user_not_found()
    {
        _users.GetByEmailAsync(Arg.Any<string>(), default).ReturnsNull();

        var outcome = await _sut.LoginAsync("nobody@example.com", "pass");

        Assert.Null(outcome.Response);
        Assert.NotNull(outcome.Error);
        // Unknown email must not cause any DB write
        _users.DidNotReceive().Update(Arg.Any<User>());
        await _users.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task LoginAsync_returns_error_when_password_wrong()
    {
        var user = MakeUser();
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("wrong_password", "hash").Returns(false);

        var outcome = await _sut.LoginAsync("test@example.com", "wrong_password");

        Assert.Null(outcome.Response);
        Assert.NotNull(outcome.Error);
        await _tokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), default);
    }

    [Fact]
    public async Task LoginAsync_returns_error_when_user_is_inactive()
    {
        var user = MakeInactiveUser();
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var outcome = await _sut.LoginAsync("test@example.com", "pass");

        Assert.Null(outcome.Response);
        Assert.NotNull(outcome.Error);
    }

    // ── Account lockout (TASK-329) ─────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_wrong_password_increments_failure_counter()
    {
        var user = MakeUser();
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("wrong", "hash").Returns(false);

        await _sut.LoginAsync("test@example.com", "wrong");

        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.False(user.IsLockedOut);
    }

    [Fact]
    public async Task LoginAsync_locks_account_after_five_failures()
    {
        var user = MakeUser();
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("wrong", "hash").Returns(false);

        for (var i = 0; i < 5; i++)
            await _sut.LoginAsync("test@example.com", "wrong");

        Assert.True(user.IsLockedOut);
        Assert.NotNull(user.LockoutUntil);
        Assert.Equal(0, user.FailedLoginAttempts); // counter resets when lock engages

        // "user.locked_out" audited exactly once
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(l => l.Action == "user.locked_out"), default);
        // every failure audited
        await _activityLogs.Received(5).LogAsync(
            Arg.Is<ActivityLog>(l => l.Action == "user.login_failed"), default);
    }

    [Fact]
    public async Task LoginAsync_rejects_correct_password_while_locked_out()
    {
        var user = MakeUser();
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("wrong", "hash").Returns(false);
        _hasher.Verify("correct", "hash").Returns(true);

        for (var i = 0; i < 5; i++)
            await _sut.LoginAsync("test@example.com", "wrong");

        var outcome = await _sut.LoginAsync("test@example.com", "correct");

        // Generic error — the lockout must not be revealed, no tokens issued
        Assert.Null(outcome.Response);
        Assert.Equal("Invalid email or password.", outcome.Error);
        await _tokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), default);
    }

    [Fact]
    public async Task LoginAsync_success_resets_failure_counter()
    {
        var user = MakeUser();
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("wrong", "hash").Returns(false);
        _hasher.Verify("correct", "hash").Returns(true);

        await _sut.LoginAsync("test@example.com", "wrong");
        await _sut.LoginAsync("test@example.com", "wrong");
        Assert.Equal(2, user.FailedLoginAttempts);

        var outcome = await _sut.LoginAsync("test@example.com", "correct");

        Assert.NotNull(outcome.Response);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutUntil);
    }

    // ── 2FA login gate (TASK-330) ──────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_returns_challenge_instead_of_tokens_when_totp_enabled()
    {
        var user = MakeUserWithTotp();
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("correct", "hash").Returns(true);
        _jwt.GenerateTwoFactorChallengeToken(user.Id, user.AuthVersion).Returns("challenge_jwt");

        var outcome = await _sut.LoginAsync("test@example.com", "correct");

        Assert.Null(outcome.Error);
        Assert.Null(outcome.Response);
        Assert.Equal("challenge_jwt", outcome.ChallengeToken);
        // No refresh token / cookie material issued before the second factor
        await _tokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), default);
    }

    // ── Refresh ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_returns_new_tokens_for_valid_refresh_token()
    {
        var user = MakeUser();
        var token = RefreshToken.Create(user.Id, "hashed_token", DateTime.UtcNow.AddDays(7));
        _tokens.GetByHashAsync("hashed_token", default).Returns(token);
        _users.GetByIdAsync(user.Id, default).Returns(user);
        _jwt.GenerateRefreshToken().Returns(("new_raw", "new_hash"));

        var (response, error) = await _sut.RefreshAsync("raw_token");

        Assert.Null(error);
        Assert.NotNull(response);
        Assert.Equal("access_token", response.AccessToken);
        Assert.Equal("new_raw", response.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_returns_error_for_invalid_token()
    {
        _tokens.GetByHashAsync(Arg.Any<string>(), default).ReturnsNull();

        var (response, error) = await _sut.RefreshAsync("bad_token");

        Assert.Null(response);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task RefreshAsync_reuse_of_revoked_token_revokes_all_user_sessions()
    {
        var user = MakeUser();
        var stolen = RefreshToken.Create(user.Id, "hashed_token", DateTime.UtcNow.AddDays(7));
        stolen.Revoke("rotated_to_hash"); // already rotated — presenting it again = theft
        BackdateRevocation(stolen, TimeSpan.FromMinutes(5)); // well outside the rotation grace window
        _tokens.GetByHashAsync("hashed_token", default).Returns(stolen);
        _users.GetByIdAsync(user.Id, default).Returns(user);

        var (response, error) = await _sut.RefreshAsync("raw_token");

        Assert.Null(response);
        Assert.NotNull(error);
        await _tokens.Received(1).RevokeAllSessionsAsync(user.Id, default);
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(l => l.Action == "auth.refresh_reuse_detected" && l.UserId == user.Id),
            default);
        // No new token minted for the attacker
        await _tokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), default);
    }

    [Fact]
    public async Task RefreshAsync_replay_within_rotation_grace_does_not_revoke_family()
    {
        var user = MakeUser();
        var rotated = RefreshToken.Create(user.Id, "hashed_token", DateTime.UtcNow.AddDays(7));
        rotated.Revoke("rotated_to_hash"); // just rotated — parallel refresh race
        _tokens.GetByHashAsync("hashed_token", default).Returns(rotated);

        var (response, error) = await _sut.RefreshAsync("raw_token");

        Assert.Null(response);
        Assert.NotNull(error);
        await _tokens.DidNotReceive().RevokeAllSessionsAsync(Arg.Any<Guid>(), default);
        await _tokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), default);
    }

    private static void BackdateRevocation(RefreshToken token, TimeSpan age) =>
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.RevokedAt))!
            .SetValue(token, DateTime.UtcNow - age);

    // ── Revoke ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeAsync_marks_token_as_revoked()
    {
        var user = MakeUser();
        var token = RefreshToken.Create(user.Id, "hashed_token", DateTime.UtcNow.AddDays(7));
        _tokens.GetByHashAsync("hashed_token", default).Returns(token);

        await _sut.RevokeAsync("raw_token");

        await _tokens.Received(1).RevokeSessionAsync(user.Id, token.SessionId, default);
    }

    // ── Login with a temporary password (TASK-465) ────────────────────────────

    [Fact]
    public async Task LoginAsync_valid_temp_password_succeeds_and_flags_passwordIsTemporary()
    {
        var user = MakeUser();
        user.SetTempPasswordExpiry(DateTime.UtcNow.AddHours(3));
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("Temp1234Passw", "hash").Returns(true);

        var outcome = await _sut.LoginAsync("test@example.com", "Temp1234Passw");

        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.Response);
        Assert.True(outcome.Response.User.PasswordIsTemporary);
        Assert.NotNull(outcome.Response.User.TemporaryPasswordExpiresAt);
    }

    [Fact]
    public async Task LoginAsync_expired_temp_password_returns_specific_error()
    {
        var user = MakeUser();
        user.SetTempPasswordExpiry(DateTime.UtcNow.AddHours(-1)); // already expired
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("Temp1234Passw", "hash").Returns(true);

        var outcome = await _sut.LoginAsync("test@example.com", "Temp1234Passw");

        Assert.Null(outcome.Response);
        Assert.Null(outcome.ChallengeToken);
        Assert.Equal("Temporary password has expired. Please request a new one.", outcome.Error);
        await _tokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), default);
    }

    [Fact]
    public async Task LoginAsync_wrong_password_against_expired_temp_password_stays_generic()
    {
        // The specific "temporary password has expired" error must only ever surface on a hash
        // MATCH — a wrong guess against an account with an expired temp password stays exactly
        // as generic as any other wrong-password attempt (no extra signal about temp-password
        // state leaks to someone who doesn't already hold the correct credential).
        var user = MakeUser();
        user.SetTempPasswordExpiry(DateTime.UtcNow.AddHours(-1));
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("wrong", "hash").Returns(false);

        var outcome = await _sut.LoginAsync("test@example.com", "wrong");

        Assert.Equal("Invalid email or password.", outcome.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Anonymous_login_flushes_audit_in_the_persisted_users_tenant(bool validPassword)
    {
        var user = MakeUser(); var flushed = false;
        _users.GetByEmailAsync(user.Email, default).Returns(user);
        _hasher.Verify("synthetic", user.PasswordHash).Returns(validPassword);
        Task Flush() { Assert.Equal(user.TenantId, _scope.TenantId); flushed = true; return Task.CompletedTask; }
        _users.SaveChangesAsync(default).Returns(_ => Flush());
        _activityLogs.SaveChangesAsync(default).Returns(_ => Flush());
        _permissionGrants.GetActiveGrantsForUserAsync(user.TenantId!.Value, user.Id, default).Returns(_ => {
            Assert.Equal(user.TenantId, _scope.TenantId); return Array.Empty<UserPermissionGrant>(); });
        var outcome = await _sut.LoginAsync(user.Email, "synthetic");
        Assert.True(flushed); Assert.Equal(validPassword, outcome.Response != null); Assert.Null(_scope.TenantId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Anonymous_refresh_scopes_profile_or_replay_audit_without_nesting_rotation(bool replay)
    {
        var user = MakeUser(); var token = RefreshToken.Create(user.Id, "hashed_token", DateTime.UtcNow.AddDays(1));
        if (replay) { token.Revoke(); BackdateRevocation(token, TimeSpan.FromMinutes(1)); }
        _tokens.GetByHashAsync("hashed_token", default).Returns(token);
        _users.GetByIdAsync(user.Id, default).Returns(user);
        var observed = false;
        _activityLogs.SaveChangesAsync(default).Returns(_ => {
            Assert.Equal(user.TenantId, _scope.TenantId); observed = true; return Task.CompletedTask; });
        _permissionGrants.GetActiveGrantsForUserAsync(user.TenantId!.Value, user.Id, default).Returns(_ => {
            Assert.Equal(user.TenantId, _scope.TenantId); observed = true; return Array.Empty<UserPermissionGrant>(); });
        _tokens.TryRotateAsync(token, Arg.Any<RefreshToken>(), default).Returns(_ => { Assert.Null(_scope.TenantId); return true; });
        var result = await _sut.RefreshAsync("raw_token");
        Assert.True(observed); Assert.Equal(!replay, result.Response != null); Assert.Null(_scope.TenantId);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static User MakeUser() =>
        User.Create(Guid.NewGuid(), "test@example.com", "Test User", "hash", "store_manager");

    private static User MakeUserWithTotp()
    {
        var user = MakeUser();
        user.SetPendingTotpSecret("JBSWY3DPEHPK3PXP");
        user.EnableTotp(["recovery_hash"]);
        return user;
    }

    private static User MakeInactiveUser()
    {
        var user = MakeUser();
        user.Deactivate();
        return user;
    }
}
