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
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _sut = new AuthService(_users, _tokens, _hasher, _jwt, _activityLogs);
        _jwt.GenerateRefreshToken().Returns(("raw_token", "hashed_token"));
        _jwt.HashToken("raw_token").Returns("hashed_token");
        _jwt.GenerateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<string?>()).Returns("access_token");
    }

    [Fact]
    public async Task LoginAsync_returns_tokens_when_credentials_are_valid()
    {
        var user = MakeUser();
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("password123", "hash").Returns(true);

        var (response, error) = await _sut.LoginAsync("test@example.com", "password123");

        Assert.Null(error);
        Assert.NotNull(response);
        Assert.Equal("access_token", response.AccessToken);
        Assert.Equal("raw_token", response.RefreshToken);
        Assert.Equal("test@example.com", response.User.Email);
    }

    [Fact]
    public async Task LoginAsync_returns_error_when_user_not_found()
    {
        _users.GetByEmailAsync(Arg.Any<string>(), default).ReturnsNull();

        var (response, error) = await _sut.LoginAsync("nobody@example.com", "pass");

        Assert.Null(response);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task LoginAsync_returns_error_when_password_wrong()
    {
        var user = MakeUser();
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify("wrong_password", "hash").Returns(false);

        var (response, error) = await _sut.LoginAsync("test@example.com", "wrong_password");

        Assert.Null(response);
        Assert.NotNull(error);
        await _tokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), default);
    }

    [Fact]
    public async Task LoginAsync_returns_error_when_user_is_inactive()
    {
        var user = MakeInactiveUser();
        _users.GetByEmailAsync("test@example.com", default).Returns(user);
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

        var (response, error) = await _sut.LoginAsync("test@example.com", "pass");

        Assert.Null(response);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task RefreshAsync_returns_new_tokens_for_valid_refresh_token()
    {
        var user = MakeUser();
        var token = RefreshToken.Create(user.Id, "hashed_token", DateTime.UtcNow.AddDays(7));
        _tokens.GetActiveByHashAsync("hashed_token", default).Returns(token);
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
        _tokens.GetActiveByHashAsync(Arg.Any<string>(), default).ReturnsNull();

        var (response, error) = await _sut.RefreshAsync("bad_token");

        Assert.Null(response);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task RevokeAsync_marks_token_as_revoked()
    {
        var user = MakeUser();
        var token = RefreshToken.Create(user.Id, "hashed_token", DateTime.UtcNow.AddDays(7));
        _tokens.GetActiveByHashAsync("hashed_token", default).Returns(token);

        await _sut.RevokeAsync("raw_token");

        _tokens.Received(1).Update(Arg.Is<RefreshToken>(t => t.RevokedAt.HasValue));
        await _tokens.Received(1).SaveChangesAsync(default);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static User MakeUser() =>
        User.Create(Guid.NewGuid(), "test@example.com", "Test User", "hash", "store_manager");

    private static User MakeInactiveUser()
    {
        // Use reflection to set IsActive = false since the setter is private
        var user = User.Create(Guid.NewGuid(), "test@example.com", "Test User", "hash", "store_manager");
        typeof(User).GetProperty("IsActive")!
            .SetValue(user, false, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, null, null);
        return user;
    }
}
