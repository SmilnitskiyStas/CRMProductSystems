using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShelfGuard.Application.Features.ConsumerAuth;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class ConsumerSessionTests
{
    private readonly IConsumerSessionStore _store = Substitute.For<IConsumerSessionStore>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();
    private readonly ConsumerAuthService _service;
    public ConsumerSessionTests()
    {
        _service = new ConsumerAuthService(Substitute.For<IConsumerAccountRepository>(), Substitute.For<IPasswordHasher>(), _jwt,
            NullLogger<ConsumerAuthService>.Instance, _store);
        _jwt.HashToken("raw").Returns("hash"); _jwt.GenerateRefreshToken().Returns(("next-raw", "next-hash"));
        _jwt.GenerateConsumerAccessToken(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<int>()).Returns("access");
    }
    private ConsumerRefreshToken Proof()
    {
        var account = new ConsumerAccount { FullName = "Synthetic", Phone = "+380501234567" };
        return new ConsumerRefreshToken { ConsumerAccountId = account.Id, TokenHash = "hash", ExpiresAt = DateTime.UtcNow.AddDays(1), ConsumerAccount = account };
    }

    [Fact]
    public async Task Rotation_preserves_device_session_and_absolute_expiry_and_returns_new_proof_only_after_commit()
    {
        var proof = Proof(); _store.FindAsync("hash", default).Returns(proof);
        _store.RotateAsync(proof, Arg.Any<ConsumerRefreshToken>(), default).Returns(true);
        var (response, error) = await _service.RefreshAsync("raw");
        Assert.Null(error); Assert.Equal("next-raw", response!.RefreshToken);
        await _store.Received(1).RotateAsync(proof, Arg.Is<ConsumerRefreshToken>(t => t.SessionId == proof.SessionId && t.ExpiresAt == proof.ExpiresAt && t.TokenHash == "next-hash"), default);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("expired")]
    [InlineData("inactive")]
    [InlineData("version")]
    public async Task Invalid_refresh_never_mints_access_or_replacement(string state)
    {
        var proof = state == "expired" ? new ConsumerRefreshToken { ExpiresAt = DateTime.UtcNow.AddDays(-1), ConsumerAccount = new ConsumerAccount() } : Proof();
        if (state == "inactive") proof.ConsumerAccount.IsActive = false;
        if (state == "version") proof.ConsumerAccount.AuthVersion++;
        if (state != "unknown") _store.FindAsync("hash", default).Returns(proof);
        var (response, error) = await _service.RefreshAsync("raw");
        Assert.Null(response); Assert.NotNull(error);
        _jwt.DidNotReceive().GenerateRefreshToken();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rotated_replay_grace_never_reissues_proof_and_old_replay_revokes_only_its_family(bool recent)
    {
        var proof = Proof(); proof.RevokedAt = DateTime.UtcNow.AddMinutes(recent ? 0 : -5); proof.ReplacedByTokenHash = "replacement";
        _store.FindAsync("hash", default).Returns(proof);
        var (response, error) = await _service.RefreshAsync("raw");
        Assert.Null(response); Assert.NotNull(error);
        if (recent) { Assert.Equal(ConsumerAuthService.RotationRetry, error); await _store.DidNotReceiveWithAnyArgs().RevokeAsync(default, default, default); }
        else await _store.Received(1).RevokeAsync(proof.ConsumerAccountId, proof.SessionId, default);
        _jwt.DidNotReceive().GenerateRefreshToken();
    }

    [Fact]
    public void Consumer_JWT_is_short_lived_tenant_free_and_challenge_contains_auth_version()
    {
        var jwt = new JwtService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Jwt:Secret"] = new string('S', 64), ["Jwt:ConsumerAccessTokenMinutes"] = "43200" }).Build());
        var id = Guid.NewGuid(); var sid = Guid.NewGuid(); var before = DateTime.UtcNow;
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt.GenerateConsumerAccessToken(id, sessionId: sid, authVersion: 7));
        Assert.InRange(token.ValidTo - before, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        Assert.Equal(sid.ToString(), token.Claims.Single(c => c.Type == "sid").Value);
        Assert.DoesNotContain(token.Claims, c => c.Type == "tenant_id");
        Assert.Equal(new TwoFactorProof(id, 7), jwt.ValidateTwoFactorChallengeToken(jwt.GenerateTwoFactorChallengeToken(id, 7)));
        Assert.Null(jwt.ValidateTwoFactorChallengeToken("malformed"));
    }
}
