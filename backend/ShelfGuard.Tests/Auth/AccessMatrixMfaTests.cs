using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class AccessMatrixMfaTests
{
    private const string Key = "SYNTHETIC-MFA-TEST-KEY-000000000000000000000000000000";
    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository sessions = Substitute.For<IRefreshTokenRepository>();
    private readonly ITotpService totp = Substitute.For<ITotpService>();
    private readonly IMfaPolicy policy = Substitute.For<IMfaPolicy>();
    private readonly IActivityLogRepository logs = Substitute.For<IActivityLogRepository>();
    private readonly JwtService jwt = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Jwt:Secret"] = Key, ["Jwt:Issuer"] = "matrix", ["Jwt:Audience"] = "matrix" }).Build());
    private readonly User user = User.Create(null, "synthetic@example.test", "Synthetic", "hash", "provider");
    private readonly AuthService sut;
    public AccessMatrixMfaTests()
    {
        var hasher = Substitute.For<IPasswordHasher>(); hasher.Verify("password", "hash").Returns(true);
        users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        policy.IsRequiredAsync(user, Arg.Any<CancellationToken>()).Returns(true);
        totp.GenerateSecret().Returns("SYNTHETICSECRET");
        totp.VerifyCode("SYNTHETICSECRET", "123456").Returns(100L);
        sut = new(users, sessions, hasher, jwt, logs, totp,
            Substitute.For<IUserPermissionGrantRepository>(), Substitute.For<ITenantRoleRepository>(), NullLogger<AuthService>.Instance, policy, new AuthTenantScope());
    }
    [Fact]
    public async Task Password_only_login_for_required_role_issues_enrollment_proof_without_access_refresh_or_cookie_grant()
    {
        var outcome = await sut.LoginAsync(user.Email, "password");
        Assert.Null(outcome.Error); Assert.Null(outcome.Response); Assert.Null(outcome.ChallengeToken);
        Assert.NotNull(outcome.EnrollmentToken);
        await sessions.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        var token = new JwtSecurityTokenHandler().ReadJwtToken(outcome.EnrollmentToken);
        Assert.Equal("matrix:mfa-enrollment", Assert.Single(token.Audiences));
        Assert.True(token.ValidTo <= DateTime.UtcNow.AddMinutes(5));
        Assert.DoesNotContain(token.Claims, c => c.Type is "role" or "sid" or "tenant_id");
        Assert.Throws<SecurityTokenInvalidAudienceException>(() => new JwtSecurityTokenHandler().ValidateToken(outcome.EnrollmentToken,
            new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = "matrix", ValidateAudience = true,
                ValidAudience = "matrix", ValidateLifetime = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)) }, out _));
    }
    [Fact]
    public async Task Proofs_are_bound_to_purpose_identity_and_current_security_version()
    {
        Assert.Null(jwt.ValidateMfaEnrollmentToken(jwt.GenerateTwoFactorChallengeToken(user.Id)));
        Assert.Null(jwt.ValidateTwoFactorChallengeToken(jwt.GenerateMfaEnrollmentToken(user.Id, user.AuthVersion)));
        Assert.Null(jwt.ValidateMfaEnrollmentToken(jwt.GenerateAccessToken(user.Id, user.Email, user.Role, null, null)));
        var proof = jwt.GenerateMfaEnrollmentToken(user.Id, user.AuthVersion);
        user.ChangePassword("changed");
        Assert.NotNull((await sut.SetupEnrollmentAsync(proof)).Error);
        await users.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Enrollment_requires_valid_OTP_before_minting_a_verified_session_and_cannot_be_replayed()
    {
        var proof = jwt.GenerateMfaEnrollmentToken(user.Id, user.AuthVersion);
        Assert.Null((await sut.SetupEnrollmentAsync(proof)).Error);
        Assert.NotNull((await sut.EnableEnrollmentAsync(proof, "wrong")).Error);
        await sessions.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        var result = await sut.EnableEnrollmentAsync(proof, "123456");
        Assert.Null(result.Error); Assert.NotNull(result.Session); Assert.Equal(8, result.RecoveryCodes!.Count);
        Assert.True(user.TotpEnabled);
        Assert.All(result.RecoveryCodes, code => Assert.DoesNotContain(code, user.TotpRecoveryCodes!));
        await sessions.Received(1).AddAsync(Arg.Is<RefreshToken>(t => t.AuthVersion == user.AuthVersion && t.MfaVerifiedAt != null), Arg.Any<CancellationToken>());
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(result.Session.AccessToken).Claims, c => c.Type == "mfa_at");
        Assert.NotNull((await sut.EnableEnrollmentAsync(proof, "123456")).Error);
    }
    [Fact]
    public async Task Required_MFA_cannot_be_disabled_even_with_valid_password_and_second_factor()
    {
        user.SetPendingTotpSecret("SYNTHETICSECRET"); user.EnableTotp([jwt.HashToken("ABCD2345")]);
        Assert.NotNull(await sut.DisableTwoFactorAsync(user.Id, "password", "123456"));
        Assert.True(user.TotpEnabled); Assert.Single(user.TotpRecoveryCodes!);
    }
    [Fact]
    public async Task Unverified_existing_session_cannot_upgrade_by_refresh()
    {
        var session = RefreshToken.Create(user.Id, jwt.HashToken("raw"), DateTime.UtcNow.AddDays(1));
        sessions.GetByHashAsync(session.TokenHash, Arg.Any<CancellationToken>()).Returns(session);
        Assert.NotNull((await sut.RefreshAsync("raw")).Error);
        await sessions.DidNotReceive().TryRotateAsync(Arg.Any<RefreshToken>(), Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Refresh_preserves_original_MFA_time_instead_of_silently_extending_step_up()
    {
        user.SetPendingTotpSecret("SYNTHETICSECRET"); user.EnableTotp([]);
        var at = DateTime.UtcNow.AddMinutes(-20);
        var session = RefreshToken.Create(user.Id, jwt.HashToken("raw"), DateTime.UtcNow.AddDays(1), authVersion: user.AuthVersion, mfaVerifiedAt: at);
        sessions.GetByHashAsync(session.TokenHash, Arg.Any<CancellationToken>()).Returns(session);
        sessions.TryRotateAsync(Arg.Any<RefreshToken>(), Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>()).Returns(true);
        var result = await sut.RefreshAsync("raw"); Assert.Null(result.Error);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Response!.AccessToken);
        Assert.Equal(new DateTimeOffset(at).ToUnixTimeSeconds().ToString(), token.Claims.Single(c => c.Type == "mfa_at").Value);
        await sessions.Received(1).TryRotateAsync(session, Arg.Is<RefreshToken>(t => t.MfaVerifiedAt == at && t.ExpiresAt == session.ExpiresAt), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Step_up_consumes_one_code_updates_only_the_current_session_and_rejects_replay()
    {
        user.SetPendingTotpSecret("SYNTHETICSECRET"); user.EnableTotp([]);
        var session = RefreshToken.Create(user.Id, "hash", DateTime.UtcNow.AddDays(1), authVersion: user.AuthVersion, mfaVerifiedAt: DateTime.UtcNow.AddHours(-1));
        sessions.GetSessionAsync(user.Id, session.SessionId, Arg.Any<CancellationToken>()).Returns(session);
        sessions.ConfirmMfaAsync(user, session.SessionId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        Assert.NotNull((await sut.StepUpAsync(user.Id, session.SessionId, "bad")).Error);
        var result = await sut.StepUpAsync(user.Id, session.SessionId, "123456"); Assert.Null(result.Error);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Response!.AccessToken);
        Assert.Equal(session.SessionId.ToString(), token.Claims.Single(c => c.Type == "sid").Value);
        Assert.NotNull((await sut.StepUpAsync(user.Id, session.SessionId, "123456")).Error);
        await sessions.Received(1).ConfirmMfaAsync(user, session.SessionId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await sessions.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }
}
