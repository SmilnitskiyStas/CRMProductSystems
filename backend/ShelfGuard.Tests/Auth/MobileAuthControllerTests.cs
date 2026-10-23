using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Auth.Dtos;
using ShelfGuard.Application.Features.ConsumerAuth;
using ShelfGuard.Application.Features.ConsumerAuth.Dtos;
using ShelfGuard.Application.Features.MobileAuth.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class MobileAuthControllerTests
{
    private readonly IAuthService _staffAuth = Substitute.For<IAuthService>();
    private readonly IConsumerAuthService _consumerAuth = Substitute.For<IConsumerAuthService>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IConsumerAccountRepository _consumers = Substitute.For<IConsumerAccountRepository>();
    private readonly MobileAuthController _controller;

    public MobileAuthControllerTests()
    {
        _controller = new MobileAuthController(_staffAuth, _consumerAuth, _users, _consumers)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    public static IEnumerable<object[]> ContactCollisions()
    {
        foreach (var byEmail in new[] { false, true })
        foreach (var role in new[] { "staff", "enterprise_admin", "provider" })
        foreach (var mfaEnabled in new[] { false, true })
            yield return new object[] { byEmail, role, mfaEnabled };
    }

    [Theory]
    [MemberData(nameof(ContactCollisions))]
    public async Task Consumer_login_with_staff_contact_never_grants_workspace_or_staff_challenge(
        bool byEmail, string role, bool mfaEnabled)
    {
        var account = ArrangeContactCollision(byEmail, role, mfaEnabled);
        _consumers.GetByPhoneAsync(account.Phone, default).Returns(account);
        _consumers.GetByEmailAsync(account.Email!, default).Returns(account);
        _consumerAuth.LoginAsync(Arg.Any<ConsumerLoginRequest>(), default)
            .Returns((new ConsumerAuthResponse("consumer-token", account.Id, account.FullName, account.Phone), null));
        _users.ClearReceivedCalls();

        var result = await _controller.Login(
            new MobileLoginRequest(byEmail ? account.Email! : "050 123 45 67", "personal-password"), default);

        AssertPersonalOnly(result, account);
        Assert.Empty(_users.ReceivedCalls());
        Assert.Empty(_staffAuth.ReceivedCalls());
        await _consumerAuth.Received(1).LoginAsync(
            new ConsumerLoginRequest(account.Phone, "personal-password"), default);
    }

    [Theory]
    [MemberData(nameof(ContactCollisions))]
    public async Task Registration_with_staff_contact_never_grants_workspace_or_staff_challenge(
        bool byEmail, string role, bool mfaEnabled)
    {
        var account = ArrangeContactCollision(byEmail, role, mfaEnabled);
        var request = new ConsumerRegisterRequest(account.Phone, "StrongPassword1", account.FullName, account.Email);
        _consumerAuth.RegisterAsync(request, default)
            .Returns((new ConsumerAuthResponse("consumer-token", account.Id, account.FullName, account.Phone), null));
        _consumers.GetByIdAsync(account.Id, default).Returns(account);
        _users.ClearReceivedCalls();
        _consumers.ClearReceivedCalls();

        var result = await _controller.Register(request, default);

        AssertPersonalOnly(result, account);
        Assert.Empty(_users.ReceivedCalls());
        Assert.Empty(_staffAuth.ReceivedCalls());
        Assert.Empty(_consumers.ReceivedCalls());
    }

    [Fact]
    public async Task Changing_consumer_email_to_staff_email_does_not_grant_workspace_access()
    {
        var account = ArrangeContactCollision(true, "enterprise_admin", false);
        _consumers.GetByPhoneAsync(account.Phone, default).Returns(account);
        _consumerAuth.LoginAsync(Arg.Any<ConsumerLoginRequest>(), default)
            .Returns((new ConsumerAuthResponse("consumer-token", account.Id, account.FullName, account.Phone), null));
        var claimedStaffEmail = account.Email;
        account.Email = "personal@example.com";
        _users.ClearReceivedCalls();

        AssertPersonalOnly(await _controller.Login(new MobileLoginRequest(account.Phone, "personal-password"), default), account);
        account.Email = claimedStaffEmail;
        AssertPersonalOnly(await _controller.Login(new MobileLoginRequest(account.Phone, "personal-password"), default), account);
        account.Email = null;
        AssertPersonalOnly(await _controller.Login(new MobileLoginRequest(account.Phone, "personal-password"), default), account);

        Assert.Empty(_users.ReceivedCalls());
        Assert.Empty(_staffAuth.ReceivedCalls());
    }

    [Fact]
    public async Task Failed_consumer_login_does_not_fall_back_to_staff_authentication()
    {
        var account = ArrangeContactCollision(false, "enterprise_admin", false);
        _consumers.GetByPhoneAsync(account.Phone, default).Returns(account);
        _consumerAuth.LoginAsync(Arg.Any<ConsumerLoginRequest>(), default)
            .Returns(((ConsumerAuthResponse?)null, "Invalid phone number or password."));
        _users.ClearReceivedCalls();

        var result = await _controller.Login(new MobileLoginRequest(account.Phone, "wrong-password"), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Empty(_users.ReceivedCalls());
        Assert.Empty(_staffAuth.ReceivedCalls());
    }

    [Theory]
    [InlineData("An account with this phone number already exists.", 409)]
    [InlineData("Invalid phone number.", 400)]
    public async Task Failed_registration_does_not_issue_any_session(string error, int expectedStatus)
    {
        var request = new ConsumerRegisterRequest("+380501234567", "StrongPassword1", "Consumer");
        _consumerAuth.RegisterAsync(request, default).Returns(((ConsumerAuthResponse?)null, error));

        var result = await _controller.Register(request, default);

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.Empty(_staffAuth.ReceivedCalls());
        Assert.Empty(_users.ReceivedCalls());
    }

    private ConsumerAccount ArrangeContactCollision(bool byEmail, string role, bool mfaEnabled)
    {
        var account = new ConsumerAccount
        {
            Phone = "+380501234567", Email = "claimed@example.com", FullName = "Consumer", PasswordHash = "consumer-hash",
        };
        var staff = User.Create(role == "provider" ? null : Guid.NewGuid(),
            byEmail ? account.Email : "staff@example.com", "Staff", "different-staff-hash", role);
        staff.UpdateProfile("Staff", byEmail ? "+380671234567" : account.Phone);
        if (mfaEnabled)
        {
            staff.SetPendingTotpSecret("SYNTHETIC-SECRET");
            staff.EnableTotp(new List<string>());
        }
        _users.GetByPhoneAsync(staff.Phone!, default).Returns(staff);
        _users.GetByEmailAsync(staff.Email, default).Returns(staff);
        return account;
    }

    private static void AssertPersonalOnly(IActionResult result, ConsumerAccount account)
    {
        var response = Assert.IsType<MobileLoginResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("consumer-token", response.PersonalAccessToken);
        Assert.Null(response.WorkspaceAccessToken);
        Assert.False(response.Access.CanAccessWorkspace);
        Assert.Equal("consumer", response.Access.Role);
        Assert.Empty(response.Access.Permissions);
        Assert.Empty(response.Access.Capabilities);
        Assert.Empty(response.Access.Tabs);
        Assert.Equal(account.Id, response.User.Id);
        Assert.Null(response.User.TenantId);
        Assert.Null(response.User.StoreId);
    }

    [Fact]
    public async Task Consumer_email_without_link_stays_personal_only()
    {
        var account = new ConsumerAccount
        {
            Phone = "+380501234567", Email = "person@example.com", FullName = "Person", PasswordHash = "hash",
        };
        _consumers.GetByEmailAsync("person@example.com", default).Returns(account);
        _consumerAuth.LoginAsync(Arg.Any<ConsumerLoginRequest>(), default)
            .Returns((new ConsumerAuthResponse("consumer-token", account.Id, account.FullName, account.Phone), null));
        _users.GetByPhoneAsync(account.Phone, default).ReturnsNull();
        _users.GetByEmailAsync(account.Email, default).ReturnsNull();

        var result = await _controller.Login(
            new MobileLoginRequest("person@example.com", "password"), default);

        var response = Assert.IsType<MobileLoginResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(response.Access.CanAccessWorkspace);
        Assert.Equal("consumer-token", response.PersonalAccessToken);
        Assert.Null(response.WorkspaceAccessToken);
    }

    [Fact]
    public async Task Staff_without_personal_account_can_use_phone_as_fallback()
    {
        var staff = User.Create(Guid.NewGuid(), "worker@example.com", "Worker", "hash", "staff");
        staff.UpdateProfile("Worker", "+380501234567");
        _consumers.GetByPhoneAsync(staff.Phone!, default).ReturnsNull();
        _users.GetByPhoneAsync(staff.Phone!, default).Returns(staff);
        _staffAuth.LoginAsync(staff.Email, "password", Arg.Any<string?>(), default)
            .Returns(new LoginOutcome(new LoginResponse("staff-token", "refresh", StaffDto(staff)), null, null));

        var result = await _controller.Login(
            new MobileLoginRequest("+380501234567", "password"), default);

        var response = Assert.IsType<MobileLoginResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(response.Access.CanAccessWorkspace);
        Assert.Null(response.PersonalAccessToken);
        Assert.Equal("staff-token", response.WorkspaceAccessToken);
        await _staffAuth.Received(1).LoginAsync(staff.Email, "password", Arg.Any<string?>(), default);
    }

    [Fact]
    public async Task Staff_only_fallback_two_factor_challenge_has_no_personal_token_field()
    {
        var staff = User.Create(Guid.NewGuid(), "worker@example.com", "Worker", "hash", "staff");
        staff.UpdateProfile("Worker", "+380501234567");
        _consumers.GetByPhoneAsync(staff.Phone!, default).ReturnsNull();
        _users.GetByPhoneAsync(staff.Phone!, default).Returns(staff);
        _staffAuth.LoginAsync(staff.Email, "password", Arg.Any<string?>(), default)
            .Returns(new LoginOutcome(null, "challenge-token", null));

        var result = await _controller.Login(
            new MobileLoginRequest("+380501234567", "password"), default);

        var value = Assert.IsType<OkObjectResult>(result).Value!;
        Assert.Equal(true, value.GetType().GetProperty("requiresTwoFactor")!.GetValue(value));
        Assert.Equal("challenge-token", value.GetType().GetProperty("challengeToken")!.GetValue(value));
        Assert.Null(value.GetType().GetProperty("personalAccessToken"));
    }

    private static AuthUserDto StaffDto(User user) => new(
        user.Id, user.Email, user.FullName, user.Role, user.TenantId, null, user.StoreId,
        new Dictionary<string, bool>());
}
