using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Application.Features.ConsumerProfile;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.ConsumerProfile;

public sealed class ConsumerProfileServiceTests
{
    private readonly IConsumerAccountRepository _accounts = Substitute.For<IConsumerAccountRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ConsumerProfileService _sut;

    public ConsumerProfileServiceTests()
    {
        _sut = new ConsumerProfileService(_accounts, _hasher, NullLogger<ConsumerProfileService>.Instance);
    }

    private static ConsumerAccount MakeAccount(
        string phone = "+380501234567", string? email = "old@example.com", bool isActive = true) => new()
    {
        Phone = phone,
        PasswordHash = "hash",
        FullName = "Тест Тестенко",
        Email = email,
        IsActive = isActive,
    };

    // ── GetProfileAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetProfileAsync_unknown_consumer_returns_404()
    {
        var id = Guid.NewGuid();
        _accounts.GetByIdAsync(id, default).ReturnsNull();

        var (profile, error, statusCode) = await _sut.GetProfileAsync(id);

        Assert.Null(profile);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetProfileAsync_returns_dto_for_active_account()
    {
        var id = Guid.NewGuid();
        var account = MakeAccount();
        _accounts.GetByIdAsync(id, default).Returns(account);

        var (profile, error, statusCode) = await _sut.GetProfileAsync(id);

        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.Equal(account.FullName, profile.FullName);
        Assert.Equal(account.Email, profile.Email);
        Assert.Equal(account.Phone, profile.Phone);
    }

    // ── UpdateNameOrEmailAsync ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateNameOrEmailAsync_changed_full_name_writes_audit_row_and_saves_once()
    {
        var id = Guid.NewGuid();
        var account = MakeAccount();
        _accounts.GetByIdAsync(id, default).Returns(account);

        var (profile, error, statusCode) = await _sut.UpdateNameOrEmailAsync(id, "Новe Ім'я", null);

        Assert.Null(error);
        Assert.Equal("Новe Ім'я", profile!.FullName);
        await _accounts.Received(1).AddProfileChangeAsync(
            Arg.Is<ConsumerAccountProfileChange>(c =>
                c.ConsumerAccountId == account.Id &&
                c.FieldName == ConsumerAccountProfileChangeField.FullName &&
                c.OldValue == "Тест Тестенко" &&
                c.NewValue == "Новe Ім'я"),
            default);
        _accounts.Received(1).Update(account);
        await _accounts.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task UpdateNameOrEmailAsync_changed_email_writes_audit_row()
    {
        var id = Guid.NewGuid();
        var account = MakeAccount(email: "old@example.com");
        _accounts.GetByIdAsync(id, default).Returns(account);
        _accounts.GetByEmailAsync("new@example.com", default).ReturnsNull();

        var (profile, error, statusCode) = await _sut.UpdateNameOrEmailAsync(id, null, "New@Example.com");

        Assert.Null(error);
        Assert.Equal("new@example.com", profile!.Email);
        await _accounts.Received(1).AddProfileChangeAsync(
            Arg.Is<ConsumerAccountProfileChange>(c =>
                c.FieldName == ConsumerAccountProfileChangeField.Email &&
                c.OldValue == "old@example.com" &&
                c.NewValue == "new@example.com"),
            default);
        await _accounts.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task UpdateNameOrEmailAsync_no_actual_change_writes_no_audit_row_or_save()
    {
        var id = Guid.NewGuid();
        var account = MakeAccount();
        _accounts.GetByIdAsync(id, default).Returns(account);

        var (profile, error, statusCode) = await _sut.UpdateNameOrEmailAsync(id, account.FullName, account.Email);

        Assert.Null(error);
        Assert.NotNull(profile);
        await _accounts.DidNotReceive().AddProfileChangeAsync(Arg.Any<ConsumerAccountProfileChange>(), default);
        await _accounts.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task UpdateNameOrEmailAsync_blank_full_name_returns_400_without_writing()
    {
        var id = Guid.NewGuid();
        _accounts.GetByIdAsync(id, default).Returns(MakeAccount());

        var (profile, error, statusCode) = await _sut.UpdateNameOrEmailAsync(id, "   ", null);

        Assert.Null(profile);
        Assert.Equal(400, statusCode);
        await _accounts.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task UpdateNameOrEmailAsync_duplicate_email_returns_409_without_writing()
    {
        var id = Guid.NewGuid();
        var account = MakeAccount(email: "old@example.com");
        _accounts.GetByIdAsync(id, default).Returns(account);
        _accounts.GetByEmailAsync("taken@example.com", default)
            .Returns(new ConsumerAccount { Phone = "+380671234567", Email = "taken@example.com", FullName = "Other", PasswordHash = "h" });

        var (profile, error, statusCode) = await _sut.UpdateNameOrEmailAsync(id, null, "taken@example.com");

        Assert.Null(profile);
        Assert.Equal(409, statusCode);
        await _accounts.DidNotReceive().AddProfileChangeAsync(Arg.Any<ConsumerAccountProfileChange>(), default);
        await _accounts.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task UpdateNameOrEmailAsync_unknown_consumer_returns_404()
    {
        var id = Guid.NewGuid();
        _accounts.GetByIdAsync(id, default).ReturnsNull();

        var (profile, error, statusCode) = await _sut.UpdateNameOrEmailAsync(id, "Name", null);

        Assert.Null(profile);
        Assert.Equal(404, statusCode);
    }

    // ── ChangePhoneAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ChangePhoneAsync_correct_password_and_free_phone_succeeds_and_writes_audit_row()
    {
        var id = Guid.NewGuid();
        var account = MakeAccount(phone: "+380501234567");
        _accounts.GetByIdAsync(id, default).Returns(account);
        _hasher.Verify("CorrectPass1!", "hash").Returns(true);
        _accounts.GetByPhoneAsync("+380671234567", default).ReturnsNull();

        var (profile, error, statusCode) = await _sut.ChangePhoneAsync(id, "0671234567", "CorrectPass1!");

        Assert.Null(error);
        Assert.Equal("+380671234567", profile!.Phone);
        await _accounts.Received(1).AddProfileChangeAsync(
            Arg.Is<ConsumerAccountProfileChange>(c =>
                c.FieldName == ConsumerAccountProfileChangeField.Phone &&
                c.OldValue == "+380501234567" &&
                c.NewValue == "+380671234567"),
            default);
        _accounts.Received(1).Update(account);
        await _accounts.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task ChangePhoneAsync_wrong_password_is_rejected_without_writing()
    {
        var id = Guid.NewGuid();
        var account = MakeAccount();
        _accounts.GetByIdAsync(id, default).Returns(account);
        _hasher.Verify("WrongPass", "hash").Returns(false);

        var (profile, error, statusCode) = await _sut.ChangePhoneAsync(id, "0671234567", "WrongPass");

        Assert.Null(profile);
        Assert.NotNull(error);
        Assert.Equal(400, statusCode);
        await _accounts.DidNotReceive().AddProfileChangeAsync(Arg.Any<ConsumerAccountProfileChange>(), default);
        await _accounts.DidNotReceive().SaveChangesAsync(default);
        // Duplicate check must never run once the password itself already failed.
        await _accounts.DidNotReceive().GetByPhoneAsync(Arg.Any<string>(), default);
    }

    [Fact]
    public async Task ChangePhoneAsync_duplicate_phone_is_rejected_without_writing()
    {
        var id = Guid.NewGuid();
        var account = MakeAccount(phone: "+380501234567");
        _accounts.GetByIdAsync(id, default).Returns(account);
        _hasher.Verify("CorrectPass1!", "hash").Returns(true);
        _accounts.GetByPhoneAsync("+380671234567", default).Returns(
            new ConsumerAccount { Phone = "+380671234567", FullName = "Other", PasswordHash = "h" });

        var (profile, error, statusCode) = await _sut.ChangePhoneAsync(id, "0671234567", "CorrectPass1!");

        Assert.Null(profile);
        Assert.Equal(409, statusCode);
        await _accounts.DidNotReceive().AddProfileChangeAsync(Arg.Any<ConsumerAccountProfileChange>(), default);
        await _accounts.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task ChangePhoneAsync_malformed_phone_returns_400()
    {
        var id = Guid.NewGuid();
        var account = MakeAccount();
        _accounts.GetByIdAsync(id, default).Returns(account);
        _hasher.Verify("CorrectPass1!", "hash").Returns(true);

        var (profile, error, statusCode) = await _sut.ChangePhoneAsync(id, "not-a-phone", "CorrectPass1!");

        Assert.Null(profile);
        Assert.Equal(400, statusCode);
        await _accounts.DidNotReceive().SaveChangesAsync(default);
    }

    [Fact]
    public async Task ChangePhoneAsync_same_normalized_phone_is_a_noop_without_writing()
    {
        var id = Guid.NewGuid();
        var account = MakeAccount(phone: "+380501234567");
        _accounts.GetByIdAsync(id, default).Returns(account);
        _hasher.Verify("CorrectPass1!", "hash").Returns(true);

        var (profile, error, statusCode) = await _sut.ChangePhoneAsync(id, "0501234567", "CorrectPass1!");

        Assert.Null(error);
        Assert.Equal("+380501234567", profile!.Phone);
        await _accounts.DidNotReceive().AddProfileChangeAsync(Arg.Any<ConsumerAccountProfileChange>(), default);
        await _accounts.DidNotReceive().SaveChangesAsync(default);
    }

    // ── GetProfileChangeHistoryAsync ───────────────────────────────────────

    [Fact]
    public async Task GetProfileChangeHistoryAsync_unknown_consumer_returns_404()
    {
        var id = Guid.NewGuid();
        _accounts.GetByIdAsync(id, default).ReturnsNull();

        var (history, error, statusCode) = await _sut.GetProfileChangeHistoryAsync(id, 1, 50);

        Assert.Null(history);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task GetProfileChangeHistoryAsync_returns_paged_items()
    {
        var id = Guid.NewGuid();
        _accounts.GetByIdAsync(id, default).Returns(MakeAccount());
        var rows = new List<ConsumerAccountProfileChange>
        {
            new()
            {
                ConsumerAccountId = id, FieldName = ConsumerAccountProfileChangeField.Phone,
                OldValue = "+380501234567", NewValue = "+380671234567",
            },
        };
        _accounts.GetProfileChangesPagedAsync(id, 1, 50, default).Returns((rows, 1));

        var (history, error, statusCode) = await _sut.GetProfileChangeHistoryAsync(id, 1, 50);

        Assert.Null(error);
        Assert.NotNull(history);
        Assert.Single(history.Items);
        Assert.Equal(1, history.TotalCount);
        Assert.Equal(ConsumerAccountProfileChangeField.Phone, history.Items[0].FieldName);
    }
}
