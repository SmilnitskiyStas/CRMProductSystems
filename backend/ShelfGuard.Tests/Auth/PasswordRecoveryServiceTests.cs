using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Auth.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class PasswordRecoveryServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string Token = new('A', 43);
    private const string NewPassword = "New-safe-pass-2026";
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordRecoveryStore _store = Substitute.For<IPasswordRecoveryStore>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly PasswordRecoveryService _service;

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    public PasswordRecoveryServiceTests()
    {
        _service = new PasswordRecoveryService(_users, _store, _hasher,
            new PasswordRecoveryOptions("https://web.example"), new Clock(), NullLogger<PasswordRecoveryService>.Instance);
        _hasher.Hash(NewPassword).Returns("new-password-hash");
    }

    private User Account(bool grant = false)
    {
        var user = User.Create(null, "person@example.com", "Synthetic user", "old-password-hash", "provider");
        if (grant) user.SetPasswordResetGrant(PasswordRecoveryService.Hash(Token), Now, Now.AddMinutes(15));
        _users.GetByEmailAsync(user.Email, default).Returns(user);
        return user;
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://web.example")]
    [InlineData("https://user:secret@web.example")]
    [InlineData("https://web.example?returnTo=other")]
    [InlineData("https://web.example#fragment")]
    public async Task Unsafe_or_missing_configured_web_origin_is_rejected_before_lookup(string origin)
    {
        var service = new PasswordRecoveryService(_users, _store, _hasher,
            new PasswordRecoveryOptions(origin), new Clock(), NullLogger<PasswordRecoveryService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestAsync("person@example.com"));
        Assert.Empty(_users.ReceivedCalls());
        Assert.Empty(_store.ReceivedCalls());
    }

    [Fact]
    public async Task Request_only_stores_a_hashed_short_lived_grant_and_leaves_password_lockout_and_MFA_unchanged()
    {
        var user = Account();
        user.SetPendingTotpSecret("SYNTHETIC_SECRET");
        user.EnableTotp([]);
        user.RegisterFailedLogin(5, TimeSpan.FromMinutes(15));
        NotificationQueue? queued = null;
        string? savedHash = null;
        _store.IssueAsync(user, Arg.Any<string>(), Now, Now.AddMinutes(15), Arg.Any<NotificationQueue>(), default)
            .Returns(call => { savedHash = call.ArgAt<string>(1); queued = call.ArgAt<NotificationQueue>(4); return true; });

        await _service.RequestAsync(" PERSON@EXAMPLE.COM ");

        Assert.Equal("old-password-hash", user.PasswordHash);
        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.True(user.TotpEnabled);
        Assert.Null(user.TempPasswordExpiresAt);
        Assert.NotNull(queued);
        using var json = JsonDocument.Parse(queued!.Payload!);
        var url = new Uri(json.RootElement.GetProperty("resetUrl").GetString()!);
        Assert.Equal("https://web.example/reset-password", url.GetLeftPart(UriPartial.Path));
        Assert.Empty(url.Query);
        var fragment = url.Fragment.TrimStart('#').Split('&').Select(s => s.Split('=', 2))
            .ToDictionary(s => s[0], s => Uri.UnescapeDataString(s[1]));
        Assert.Equal(user.Email, fragment["email"]);
        Assert.Equal(43, fragment["token"].Length);
        Assert.Equal(PasswordRecoveryService.Hash(fragment["token"]), savedHash);
        Assert.DoesNotContain("tempPassword", queued.Payload!);
        Assert.Empty(_hasher.ReceivedCalls());
        await _users.DidNotReceive().SaveChangesAsync(default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_or_inactive_request_has_no_store_or_credential_side_effect(bool inactive)
    {
        if (inactive) Account().Deactivate();
        await _service.RequestAsync("person@example.com");
        Assert.Empty(_store.ReceivedCalls());
        Assert.Empty(_hasher.ReceivedCalls());
    }

    [Fact]
    public async Task Storage_or_delivery_queue_fault_preserves_the_current_password_and_public_success_contract()
    {
        var user = Account();
        _store.IssueAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<NotificationQueue>(), default)
            .Returns(Task.FromException<bool>(new InvalidOperationException("Synthetic outbox unavailable")));
        await _service.RequestAsync(user.Email);
        Assert.Equal("old-password-hash", user.PasswordHash);
        Assert.Null(user.PasswordResetTokenHash);
        Assert.Empty(_hasher.ReceivedCalls());
    }

    [Theory]
    [InlineData("short")]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public async Task Malformed_proof_is_rejected_before_user_lookup(string token)
    {
        var error = await _service.ConfirmAsync(new ResetPasswordRequest("person@example.com", token, NewPassword));
        Assert.Equal(PasswordRecoveryService.InvalidGrant, error);
        Assert.Empty(_users.ReceivedCalls());
        Assert.Empty(_store.ReceivedCalls());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("expired")]
    [InlineData("mismatch")]
    [InlineData("consumed")]
    public async Task Invalid_proofs_return_the_same_error_and_do_not_consume_or_hash_password(string state)
    {
        if (state != "unknown")
        {
            var user = Account(grant: true);
            if (state == "inactive") user.Deactivate();
            if (state == "expired") user.SetPasswordResetGrant(PasswordRecoveryService.Hash(Token), Now.AddMinutes(-30), Now);
            if (state == "mismatch") user.SetPasswordResetGrant(PasswordRecoveryService.Hash(new string('B', 43)), Now, Now.AddMinutes(15));
            if (state == "consumed") user.ChangePassword("changed-password-hash");
        }
        var error = await _service.ConfirmAsync(new ResetPasswordRequest("person@example.com", Token, NewPassword));
        Assert.Equal(PasswordRecoveryService.InvalidGrant, error);
        Assert.Empty(_store.ReceivedCalls());
        Assert.Empty(_hasher.ReceivedCalls());
    }

    [Fact]
    public async Task Valid_proof_uses_atomic_confirmation_and_preserves_MFA()
    {
        var user = Account(grant: true);
        user.SetPendingTotpSecret("SYNTHETIC_SECRET");
        user.EnableTotp([]);
        _store.ConfirmAsync(user.Id, PasswordRecoveryService.Hash(Token), user.PasswordHash, "new-password-hash", Now, null, default).Returns(true);
        Assert.Null(await _service.ConfirmAsync(new ResetPasswordRequest(user.Email, Token, NewPassword)));
        Assert.True(user.TotpEnabled);
        await _store.Received(1).ConfirmAsync(user.Id, PasswordRecoveryService.Hash(Token), "old-password-hash", "new-password-hash", Now, null, default);
    }

    [Fact]
    public async Task A_lost_concurrent_consume_returns_invalid_grant()
    {
        var user = Account(grant: true);
        Assert.Equal(PasswordRecoveryService.InvalidGrant,
            await _service.ConfirmAsync(new ResetPasswordRequest(user.Email, Token, NewPassword)));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("abcdefghijklm")]
    [InlineData("person-password-2026")]
    public async Task Weak_password_does_not_consume_the_proof(string password)
    {
        var user = Account(grant: true);
        Assert.NotNull(await _service.ConfirmAsync(new ResetPasswordRequest(user.Email, Token, password)));
        Assert.Empty(_store.ReceivedCalls());
        Assert.Equal(PasswordRecoveryService.Hash(Token), user.PasswordResetTokenHash);
    }

    [Fact]
    public void A_normal_password_change_invalidates_an_outstanding_reset_grant()
    {
        var user = Account(grant: true);
        user.ChangePassword("another-password-hash");
        Assert.Null(user.PasswordResetTokenHash);
        Assert.Null(user.PasswordResetExpiresAt);
    }

    [Fact]
    public async Task Public_controller_never_returns_reset_proof_or_issues_a_login_session()
    {
        var recovery = Substitute.For<IPasswordRecoveryService>();
        recovery.ConfirmAsync(Arg.Any<ResetPasswordRequest>(), Arg.Any<string?>(), default).Returns((string?)null);
        var controller = new PasswordRecoveryController(recovery)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<NoContentResult>(await controller.ForgotPassword(new ForgotPasswordRequest("person@example.com"), default));
        Assert.IsType<NoContentResult>(await controller.Confirm(new ResetPasswordRequest("person@example.com", Token, NewPassword), default));
        Assert.Contains("refreshToken=", controller.Response.Headers.SetCookie.ToString());
    }
}
