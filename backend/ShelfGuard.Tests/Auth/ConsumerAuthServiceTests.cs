using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Application.Features.ConsumerAuth;
using ShelfGuard.Application.Features.ConsumerAuth.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class ConsumerAuthServiceTests
{
    private readonly IConsumerAccountRepository _accounts = Substitute.For<IConsumerAccountRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();
    private readonly ConsumerAuthService _sut;

    public ConsumerAuthServiceTests()
    {
        _sut = new ConsumerAuthService(_accounts, _hasher, _jwt, NullLogger<ConsumerAuthService>.Instance);
        _jwt.GenerateConsumerAccessToken(Arg.Any<Guid>(), Arg.Any<string?>()).Returns("consumer_access_token");
    }

    private static ConsumerAccount MakeAccount(string phone = "+380501234567", bool isActive = true) => new()
    {
        Phone = phone,
        PasswordHash = "hash",
        FullName = "Тест Тестенко",
        IsActive = isActive,
    };

    // ── Register ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_valid_request_creates_account_and_returns_token()
    {
        _accounts.GetByPhoneAsync("+380501234567", default).ReturnsNull();
        _hasher.Hash("StrongPassw0rd!").Returns("hashed");

        var (response, error) = await _sut.RegisterAsync(
            new ConsumerRegisterRequest("0501234567", "StrongPassw0rd!", "Тест Тестенко"));

        Assert.Null(error);
        Assert.NotNull(response);
        Assert.Equal("consumer_access_token", response.AccessToken);
        Assert.Equal("+380501234567", response.Phone);
        await _accounts.Received(1).AddAsync(
            Arg.Is<ConsumerAccount>(a => a.Phone == "+380501234567" && a.PasswordHash == "hashed"), default);
        await _accounts.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task RegisterAsync_invalid_phone_returns_error_without_touching_repo()
    {
        var (response, error) = await _sut.RegisterAsync(
            new ConsumerRegisterRequest("123", "StrongPassw0rd!", "Тест"));

        Assert.Null(response);
        Assert.NotNull(error);
        await _accounts.DidNotReceive().AddAsync(Arg.Any<ConsumerAccount>(), default);
    }

    [Fact]
    public async Task RegisterAsync_duplicate_email_is_rejected_case_insensitively()
    {
        _accounts.GetByPhoneAsync("+380501234567", default).ReturnsNull();
        _accounts.GetByEmailAsync("person@example.com", default).Returns(new ConsumerAccount
        {
            Phone = "+380671234567", Email = "person@example.com", FullName = "Existing", PasswordHash = "hash",
        });

        var (response, error) = await _sut.RegisterAsync(new ConsumerRegisterRequest(
            "0501234567", "StrongPassw0rd!", "Person", " Person@Example.com "));

        Assert.Null(response);
        Assert.Contains("email", error, StringComparison.OrdinalIgnoreCase);
        await _accounts.DidNotReceive().AddAsync(Arg.Any<ConsumerAccount>(), default);
    }

    [Fact]
    public async Task RegisterAsync_weak_password_returns_error()
    {
        var (response, error) = await _sut.RegisterAsync(
            new ConsumerRegisterRequest("0501234567", "short", "Тест"));

        Assert.Null(response);
        Assert.Contains("Password", error);
    }

    [Fact]
    public async Task RegisterAsync_missing_full_name_returns_error()
    {
        var (response, error) = await _sut.RegisterAsync(
            new ConsumerRegisterRequest("0501234567", "StrongPassw0rd!", "   "));

        Assert.Null(response);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task RegisterAsync_existing_phone_returns_conflict_error()
    {
        _accounts.GetByPhoneAsync("+380501234567", default).Returns(MakeAccount());

        var (response, error) = await _sut.RegisterAsync(
            new ConsumerRegisterRequest("0501234567", "StrongPassw0rd!", "Тест"));

        Assert.Null(response);
        Assert.Contains("already exists", error);
        await _accounts.DidNotReceive().AddAsync(Arg.Any<ConsumerAccount>(), default);
    }

    // ── Login ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_valid_credentials_returns_token()
    {
        var account = MakeAccount();
        _accounts.GetByPhoneAsync("+380501234567", default).Returns(account);
        _hasher.Verify("StrongPassw0rd!", "hash").Returns(true);

        var (response, error) = await _sut.LoginAsync(
            new ConsumerLoginRequest("0501234567", "StrongPassw0rd!"));

        Assert.Null(error);
        Assert.NotNull(response);
        Assert.Equal(0, account.FailedLoginAttempts);
        Assert.NotNull(account.LastLoginAt);
    }

    [Fact]
    public async Task LoginAsync_unknown_phone_returns_generic_error()
    {
        _accounts.GetByPhoneAsync(Arg.Any<string>(), default).ReturnsNull();

        var (response, error) = await _sut.LoginAsync(new ConsumerLoginRequest("0501234567", "whatever"));

        Assert.Null(response);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task LoginAsync_wrong_password_increments_failed_attempts()
    {
        var account = MakeAccount();
        _accounts.GetByPhoneAsync("+380501234567", default).Returns(account);
        _hasher.Verify("wrong", "hash").Returns(false);

        var (response, error) = await _sut.LoginAsync(new ConsumerLoginRequest("0501234567", "wrong"));

        Assert.Null(response);
        Assert.NotNull(error);
        Assert.Equal(1, account.FailedLoginAttempts);
        Assert.Null(account.LockoutUntil);
    }

    [Fact]
    public async Task LoginAsync_fifth_failed_attempt_locks_out_account()
    {
        var account = MakeAccount();
        account.FailedLoginAttempts = 4; // one more failure should trip the lockout
        _accounts.GetByPhoneAsync("+380501234567", default).Returns(account);
        _hasher.Verify(Arg.Any<string>(), "hash").Returns(false);

        await _sut.LoginAsync(new ConsumerLoginRequest("0501234567", "wrong"));

        Assert.Equal(0, account.FailedLoginAttempts); // counter resets on lockout
        Assert.NotNull(account.LockoutUntil);
        Assert.True(account.LockoutUntil > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task LoginAsync_locked_out_account_rejects_even_correct_password()
    {
        var account = MakeAccount();
        account.LockoutUntil = DateTimeOffset.UtcNow.AddMinutes(10);
        _accounts.GetByPhoneAsync("+380501234567", default).Returns(account);
        _hasher.Verify("StrongPassw0rd!", "hash").Returns(true); // correct password

        var (response, error) = await _sut.LoginAsync(
            new ConsumerLoginRequest("0501234567", "StrongPassw0rd!"));

        Assert.Null(response);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task LoginAsync_inactive_account_returns_generic_error()
    {
        var account = MakeAccount(isActive: false);
        _accounts.GetByPhoneAsync("+380501234567", default).Returns(account);

        var (response, error) = await _sut.LoginAsync(
            new ConsumerLoginRequest("0501234567", "whatever"));

        Assert.Null(response);
        Assert.NotNull(error);
    }
}
